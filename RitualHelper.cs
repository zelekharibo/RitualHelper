using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExileCore2;
using ExileCore2.Shared;
using ExileCore2.PoEMemory;
using ExileCore2.PoEMemory.Components;
using ExileCore2.PoEMemory.MemoryObjects;
using ExileCore2.PoEMemory.Models;
using ImGuiNET;
using ExileCore2.Shared.Enums;

namespace RitualHelper
{
    public class RitualHelper : BaseSettingsPlugin<Settings>
    {
        private const float ButtonOffset = 20f;
        private const float DebugBorderThickness = 2f;
        private const float ButtonSize = 37f;
        private const int StartActionDelayMs = 100;
        private const string ImageName = "pick.png";
        
        private static readonly Random StaticRandom = new();
        private readonly ConcurrentDictionary<RectangleF, bool?> _mouseStateForRect = new();
        private NinjaPricerBridgeService? _apiService;
        private Vector2? _originalMousePosition;
        private Func<Entity, double>? _getNinjaEntityValue;
        private Func<BaseItemType, double>? _getNinjaBaseItemTypeValue;
        private readonly List<Element> _deferredItems = new();
        private Dictionary<string, List<string>>? _uniqueArtMapping;
        private int _isDeferRunning;
        private DateTime _nextPluginBridgeRetryAt = DateTime.MinValue;
        private bool _hasLoggedMissingPluginBridge;

        private bool MoveCancellationRequested => 
            Settings.CancelWithRightClick && (Control.MouseButtons & MouseButtons.Right) != 0;


        public override bool Initialise()
        {
            try
            {
                var imagePath = Path.Combine(DirectoryFullName, "images\\pick.png").Replace('\\', '/');
                Graphics.InitImage(imagePath, false);
                
                // ensure all defer items are properly loaded into memory
                EnsureDeferItemsLoaded();
                
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Failed to initialize RitualHelper: {ex.Message}");
                return false;
            }
        }


        public override void DrawSettings()
        {
            DrawGeneralSettings();

            DrawUniqueCategorySettingsTable();
            
            ImGui.Separator();
            ImGui.Text("Defer Items");
            
            // defer group settings
            Settings.DeferGroup.DrawSettings();
        }

        private void DrawGeneralSettings()
        {
            DrawToggleSetting("Enable", Settings.Enable);
            DrawIntSliderSetting("Action Delay (ms)", Settings.ActionDelay);
            DrawIntSliderSetting("Random Delay (ms)", Settings.RandomDelay);
            DrawToggleSetting("Cancel With Right Mouse Button", Settings.CancelWithRightClick);
            DrawToggleSetting("Defer existing items", Settings.DeferExistingItems);
            DrawToggleSetting("Auto Confirm", Settings.AutoConfirm);
            DrawToggleSetting("Auto Pickup", Settings.AutoPickup);
            DrawToggleSetting("Auto Reroll", Settings.AutoReroll);
            DrawToggleSetting("Draw Debug Overlay", Settings.DrawDebugOverlay);
        }

        private void DrawUniqueCategorySettingsTable()
        {
            ImGui.Separator();
            ImGui.Text("Category Filters");

            if (!ImGui.BeginTable("UniqueCategoryFilters", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchSame))
            {
                return;
            }

            ImGui.TableSetupColumn("Category");
            ImGui.TableSetupColumn("Use");
            ImGui.TableSetupColumn("Min Ex");
            ImGui.TableHeadersRow();

            DrawUniqueCategoryRow("Currency", Settings.IncludeCurrencyItems, Settings.MinCurrencyValue);
            DrawUniqueCategoryRow("Ritual", Settings.IncludeRitualItems, Settings.MinRitualValue);
            DrawUniqueCategoryRow("Accessories", Settings.IncludeUniqueAccessories, Settings.MinUniqueAccessoriesValue);
            DrawUniqueCategoryRow("Armour", Settings.IncludeUniqueArmour, Settings.MinUniqueArmourValue);
            DrawUniqueCategoryRow("Charms", Settings.IncludeUniqueCharms, Settings.MinUniqueCharmsValue);
            DrawUniqueCategoryRow("Flasks", Settings.IncludeUniqueFlasks, Settings.MinUniqueFlasksValue);
            DrawUniqueCategoryRow("Idols", Settings.IncludeUniqueIdols, Settings.MinUniqueIdolsValue);
            DrawUniqueCategoryRow("Jewels", Settings.IncludeUniqueJewels, Settings.MinUniqueJewelsValue);
            DrawUniqueCategoryRow("Weapons", Settings.IncludeUniqueWeapons, Settings.MinUniqueWeaponsValue);

            ImGui.EndTable();
        }

        private static void DrawUniqueCategoryRow(string label, ExileCore2.Shared.Nodes.ToggleNode enabledNode, ExileCore2.Shared.Nodes.RangeNode<float> minValueNode)
        {
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted(label);

            ImGui.TableSetColumnIndex(1);
            var enabled = enabledNode.Value;
            if (ImGui.Checkbox($"##unique_enabled_{label}", ref enabled))
            {
                enabledNode.Value = enabled;
            }

            ImGui.TableSetColumnIndex(2);
            if (!enabledNode.Value)
            {
                ImGui.BeginDisabled();
            }

            var minValue = minValueNode.Value;
            if (ImGui.DragFloat($"##unique_min_{label}", ref minValue, 0.1f, minValueNode.Min, minValueNode.Max, "%.2f"))
            {
                minValueNode.Value = Math.Clamp(minValue, minValueNode.Min, minValueNode.Max);
            }

            if (!enabledNode.Value)
            {
                ImGui.EndDisabled();
            }
        }

        private static void DrawToggleSetting(string label, ExileCore2.Shared.Nodes.ToggleNode node)
        {
            var value = node.Value;
            if (ImGui.Checkbox(label, ref value))
            {
                node.Value = value;
            }
        }

        private static void DrawIntSliderSetting(string label, ExileCore2.Shared.Nodes.RangeNode<int> node)
        {
            var value = node.Value;
            if (ImGui.SliderInt(label, ref value, node.Min, node.Max))
            {
                node.Value = value;
            }
        }

        private static void DrawFloatSliderSetting(string label, ExileCore2.Shared.Nodes.RangeNode<float> node)
        {
            var value = node.Value;
            if (ImGui.SliderFloat(label, ref value, node.Min, node.Max, "%.2f"))
            {
                node.Value = value;
            }
        }

        public override void Render()
        {
            if (!Settings.Enable) return;

            var ritualPanel = GameController.IngameState.IngameUi.RitualWindow;
            if (ritualPanel?.IsVisible != true) {
                return;
            }

            if (Settings.DrawDebugOverlay.Value)
            {
                DrawRitualItemDebugOverlays();
            }

            RectangleF buttonRect;
            var rerollElement = GetRerollElement();
            if (rerollElement != null)
            {
                var rerollRect = rerollElement.GetClientRectCache;
                var rerollPos = rerollRect.TopLeft + GameController.Window.GetWindowRectangleTimeCache.TopLeft;
                var buttonY = rerollPos.Y + Math.Max(0, (rerollRect.Height - ButtonSize) / 2f);
                buttonRect = new RectangleF(
                    rerollPos.X - ButtonOffset - ButtonSize,
                    buttonY,
                    ButtonSize,
                    ButtonSize);
            }
            else
            {
                return;
            }
            
            Graphics.DrawImage(ImageName, buttonRect);

            if (IsButtonPressed(buttonRect))
            {
                _ = StartDeferAsync();
            }
        }

        public override void Tick()
        {
            EnsurePluginBridgeMethods();
        }

        private void EnsurePluginBridgeMethods()
        {
            if (_getNinjaEntityValue != null && _getNinjaBaseItemTypeValue != null)
            {
                return;
            }

            if (DateTime.Now < _nextPluginBridgeRetryAt)
            {
                return;
            }

            _getNinjaEntityValue ??= GameController.PluginBridge.GetMethod<Func<Entity, double>>("NinjaPrice.GetValue");
            _getNinjaBaseItemTypeValue ??= GameController.PluginBridge.GetMethod<Func<BaseItemType, double>>("NinjaPrice.GetBaseItemTypeValue");

            if (_getNinjaEntityValue == null || _getNinjaBaseItemTypeValue == null)
            {
                _nextPluginBridgeRetryAt = DateTime.Now.AddSeconds(2);
            }
        }

        private void DrawRitualItemDebugOverlays()
        {
            var ritualWindow = GameController.IngameState.IngameUi.RitualWindow;
            if (ritualWindow?.Items == null) return;

            var activeItems = Settings.DeferGroup.GetActiveItems().ToList();
            var windowOffset = GameController.Window.GetWindowRectangleTimeCache.TopLeft;
            var drawList = ImGui.GetForegroundDrawList();
            var includeValuableUnlisted =
                Settings.HasAnyAutomaticPricingRuleEnabled() &&
                EnsureApiServiceAvailable();

            foreach (var element in ritualWindow.Items)
            {
                if (element == null)
                {
                    continue;
                }

                var ritualElement = element;

                try
                {
                    var status = "ignore";
                    var color = new Vector4(0.6f, 0.6f, 0.6f, 1f);

                    var baseItemType = GameController.Files.BaseItemTypes.Translate(ritualElement.Item.Metadata);
                    if (baseItemType == null)
                    {
                        status = "unknown base";
                        color = new Vector4(1f, 0.2f, 0.2f, 1f);

                        var unknownRect = ritualElement.GetClientRectCache;
                        var unknownTopLeft = unknownRect.TopLeft + windowOffset;
                        var unknownBottomRight = unknownRect.BottomRight + windowOffset;
                        var unknownColor = ImGui.ColorConvertFloat4ToU32(color);
                        drawList.AddRect(unknownTopLeft, unknownBottomRight, unknownColor, 0f, ImDrawFlags.None, DebugBorderThickness);
                        drawList.AddText(new Vector2(unknownTopLeft.X, unknownTopLeft.Y - 16f), unknownColor, status);
                        continue;
                    }

                    var itemMatchName = GetItemMatchName(ritualElement.Item, baseItemType.BaseName);
                    var stackSize = ritualElement.Item.GetComponent<Stack>()?.Size ?? 1;
                    var matchingRules = activeItems
                        .Where(item => item.Enabled &&
                                       !string.IsNullOrEmpty(item.Name) &&
                                       itemMatchName.Contains(item.Name, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    var matchingItem = matchingRules.FirstOrDefault(item => stackSize >= item.MinStackSize);
                    var isAlreadyDeferred = ritualElement.Children?.Count >= 3;

                    if (matchingItem != null)
                    {
                        if (isAlreadyDeferred && !Settings.DeferExistingItems)
                        {
                            status = $"skip existing p{matchingItem.Priority}";
                            color = new Vector4(1f, 0.5f, 0.1f, 1f);
                        }
                        else
                        {
                            status = $"defer p{matchingItem.Priority}";
                            color = new Vector4(0.2f, 1f, 0.2f, 1f);
                        }
                    }
                    else if (matchingRules.Any())
                    {
                        var minRequiredStack = matchingRules.Min(item => item.MinStackSize);
                        status = $"stack {stackSize}/{minRequiredStack}";
                        color = new Vector4(1f, 0.85f, 0.2f, 1f);
                    }
                    else if (includeValuableUnlisted)
                    {
                        var uniqueCategoryThresholds = Settings.GetEnabledUniqueCategoryThresholds();
                        var minCurrencyValue = Settings.TryGetStackableCategoryThreshold("currency", out var currencyValue) ? currencyValue : (decimal?)null;
                        var minRitualValue = Settings.TryGetStackableCategoryThreshold("ritual", out var ritualValue) ? ritualValue : (decimal?)null;
                        var fallbackItem = _apiService?.TryGetFallbackDeferItemCached(
                            ritualElement.Item,
                            baseItemType,
                            itemMatchName,
                            stackSize,
                            minCurrencyValue,
                            minRitualValue,
                            uniqueCategoryThresholds);
                        if (fallbackItem != null)
                        {
                            status = $"auto p{fallbackItem.Priority}";
                            color = new Vector4(0.3f, 0.8f, 1f, 1f);
                        }
                    }

                    var rect = ritualElement.GetClientRectCache;
                    var topLeft = rect.TopLeft + windowOffset;
                    var bottomRight = rect.BottomRight + windowOffset;
                    var colorU32 = ImGui.ColorConvertFloat4ToU32(color);

                    drawList.AddRect(topLeft, bottomRight, colorU32, 0f, ImDrawFlags.None, DebugBorderThickness);
                    drawList.AddText(new Vector2(topLeft.X, topLeft.Y - 16f), colorU32, status);
                }
                catch (Exception ex)
                {
                    LogError($"error drawing ritual item debug overlay: {ex.Message}");
                }
            }
        }


        private int RandomDelay()
        {
            return StaticRandom.Next(Settings.RandomDelay);
        }

        private Element? GetDeferringElement()
        {
            var ritualWindow = GameController.IngameState.IngameUi.RitualWindow;
            if (ritualWindow?.Children == null) return null;

            foreach (var element in ritualWindow.Children)
            {
                if (element?.Children == null) continue;
                
                foreach (var subElement in element.Children)
                {
                    if (subElement?.Text == "defer item")
                    {
                        return subElement;
                    }
                }
            }

            return null;
        }


        private Element? GetCancelElement()
        {
            var ritualWindow = GameController.IngameState.IngameUi.RitualWindow;
            if (ritualWindow?.Children == null) return null;

            foreach (var element in ritualWindow.Children)
            {
                if (element?.Children == null) continue;
                
                foreach (var subElement in element.Children)
                {
                    if (subElement?.Text == "cancel")
                    {
                        return subElement;
                    }
                }
            }

            return null;
        }

        private Element? GetConfirmElement()
        {
            var ritualWindow = GameController.IngameState.IngameUi.RitualWindow;
            if (ritualWindow?.Children == null) return null;

            return FindElementWithConfirmText(ritualWindow);
        }

        private Element? FindElementWithConfirmText(Element parentElement)
        {
            if (parentElement?.Children == null) return null;

            foreach (var element in parentElement.Children)
            {
                // check if this element has "confirm" text
                if (!string.IsNullOrEmpty(element.Text) && 
                    element.Text.ToLower().Contains("confirm"))
                {
                    return element;
                }

                // recursively search children
                var result = FindElementWithConfirmText(element);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private Element? GetRerollElement()
        {
            var ritualWindow = GameController.IngameState.IngameUi.RitualWindow;
            if (ritualWindow?.Children == null) return null;

            return FindElementWithRerollTooltip(ritualWindow);
        }

        private Element? GetStartDeferringElement()
        {
            var ritualWindow = GameController.IngameState.IngameUi.RitualWindow;
            if (ritualWindow?.Children == null) return null;

            return FindElementWithDeferTooltip(ritualWindow);
        }

        private Element? FindElementWithRerollTooltip(Element parentElement)
        {
            if (parentElement?.Children == null) return null;

            foreach (var element in parentElement.Children)
            {
                // check if this element has a tooltip containing "reroll"
                if (element.Tooltip != null && 
                    !string.IsNullOrEmpty(element.Tooltip.Text) &&
                    element.Tooltip.Text.ToLower().Contains("reroll"))
                {
                    return element;
                }

                // recursively search children
                var result = FindElementWithRerollTooltip(element);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private Element? FindElementWithDeferTooltip(Element parentElement)
        {
            if (parentElement?.Children == null) return null;

            foreach (var element in parentElement.Children)
            {
                if (element.Tooltip != null &&
                    !string.IsNullOrEmpty(element.Tooltip.Text) &&
                    element.Tooltip.Text.ToLower().Contains("defer that item"))
                {
                    return element;
                }

                var result = FindElementWithDeferTooltip(element);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }



        private async Task<bool> EnterDeferringPhase()
        {
            if (GetDeferringElement() != null)
            {
                return true;
            }

            for (var attempt = 0; attempt < 5; attempt++)
            {
                var startDeferringElement = GetStartDeferringElement();
                if (startDeferringElement == null)
                {
                    await Task.Delay(50);
                    continue;
                }

                await ClickElement(startDeferringElement);

                var timeoutAt = DateTime.Now.AddMilliseconds(250);
                while (DateTime.Now < timeoutAt)
                {
                    if (GetDeferringElement() != null)
                    {
                        return true;
                    }

                    await Task.Delay(25);
                }

                await Task.Delay(Settings.ActionDelay + RandomDelay());
            }

            return GetDeferringElement() != null;
        }

        private async Task CompleteDeferringPhase()
        {
            var attempts = 0;
            while (GetDeferringElement() != null && attempts < 10)
            {
                var deferringElement = GetDeferringElement();
                if (deferringElement == null)
                {
                    break;
                }

                await ClickElement(deferringElement);

                var timeoutAt = DateTime.Now.AddMilliseconds(250);
                while (DateTime.Now < timeoutAt)
                {
                    if (GetDeferringElement() == null)
                    {
                        return;
                    }

                    await Task.Delay(25);
                }

                attempts++;
                await Task.Delay(Settings.ActionDelay + RandomDelay());
            }
        }

        private async Task StartDeferAsync()
        {
            if (Interlocked.CompareExchange(ref _isDeferRunning, 1, 0) != 0)
            {
                return;
            }

            try
            {
                while (Control.MouseButtons == MouseButtons.Left)
                {
                    await Task.Delay(10);
                }

                // clear previously deferred items list
                _deferredItems.Clear();
                await Task.Delay(StartActionDelayMs);
                var includeValuableUnlisted = Settings.HasAnyAutomaticPricingRuleEnabled();
                
                var hasActiveItems = Settings.DeferGroup.GetActiveItems().Any();
                if (includeValuableUnlisted)
                {
                    EnsureApiServiceAvailable();
                }
                
                if (Settings.DeferExistingItems || hasActiveItems || includeValuableUnlisted)
                {
                    var deferringPhaseReady = await EnterDeferringPhase();
                    if (!deferringPhaseReady)
                    {
                        LogError("failed to enter deferring phase");
                        return;
                    }
                }

                if (hasActiveItems || includeValuableUnlisted)
                {
                    await DeferItemsByPriority();
                }

                if (GetDeferringElement() != null)
                {
                    await CompleteDeferringPhase();
                }

                // auto confirm if enabled
                if (Settings.AutoConfirm.Value)
                {
                    await AutoConfirm();
                    
                    // auto pickup if both auto confirm and auto pickup are enabled
                    if (Settings.AutoPickup.Value)
                    {
                        await AutoPickup();
                    }
                    
                    // auto reroll if enabled (only works with auto confirm)
                    if (Settings.AutoReroll.Value)
                    {
                        await AutoReroll();
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"error during defer process: {ex.Message}");
            }
            finally
            {
                // clear deferred items list and restore mouse position
                _deferredItems.Clear();
                await RestoreMousePosition();
                Interlocked.Exchange(ref _isDeferRunning, 0);
            }
        }

        private async Task DeferItemsByPriority()
        {
            var activeItems = Settings.DeferGroup.GetActiveItems();
            var includeValuableUnlisted =
                Settings.HasAnyAutomaticPricingRuleEnabled() &&
                EnsureApiServiceAvailable();
            var uniqueCategoryThresholds = Settings.GetEnabledUniqueCategoryThresholds();
            var minCurrencyValue = Settings.TryGetStackableCategoryThreshold("currency", out var currencyValue) ? currencyValue : (decimal?)null;
            var minRitualValue = Settings.TryGetStackableCategoryThreshold("ritual", out var ritualValue) ? ritualValue : (decimal?)null;
            if (!activeItems.Any() && !includeValuableUnlisted) return;

            var ritualWindow = GameController.IngameState.IngameUi.RitualWindow;
            if (ritualWindow?.Items == null) return;

            // create a list of items to defer with their priorities
            var itemsToDefer = new List<(Element element, int priority, bool isExisting)>();

            foreach (var element in ritualWindow.Items)
            {
                if (MoveCancellationRequested) break;

                try
                {
                    var baseItemType = GameController.Files.BaseItemTypes.Translate(element.Item.Metadata);
                    if (baseItemType == null)
                    {
                        LogError($"unable to resolve base item type for metadata '{element.Item.Metadata}'");
                        continue;
                    }

                    var itemMatchName = GetItemMatchName(element.Item, baseItemType.BaseName);
                    var stackSize = element.Item.GetComponent<Stack>()?.Size ?? 1;
                    var matchingStackCandidates = activeItems
                        .Where(item => item.Enabled &&
                                       !string.IsNullOrEmpty(item.Name) &&
                                       itemMatchName.Contains(item.Name, StringComparison.OrdinalIgnoreCase) &&
                                       (item.MinStackSize > 1 ||
                                        itemMatchName.Contains("Chaos Orb", StringComparison.OrdinalIgnoreCase)))
                        .ToList();

                    var matchingItem = activeItems.FirstOrDefault(item => item.ShouldDefer(itemMatchName, stackSize));
                    if (matchingItem == null && includeValuableUnlisted && _apiService != null)
                    {
                        matchingItem = await _apiService.GetFallbackDeferItemAsync(
                            element.Item,
                            baseItemType,
                            itemMatchName,
                            stackSize,
                            minCurrencyValue,
                            minRitualValue,
                            uniqueCategoryThresholds);
                    }

                    if (matchingItem != null)
                    {
                        var isAlreadyDeferred = element?.Children?.Count >= 3;
                        
                        // add new items, or existing items if the setting is enabled
                        if (!isAlreadyDeferred || Settings.DeferExistingItems)
                        {
                            itemsToDefer.Add((element!, matchingItem.Priority, isAlreadyDeferred));
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogError($"error evaluating item for priority defer: {ex.Message}");
                }
            }

            // sort by priority (highest first) then by whether it's existing (new items first)
            var sortedItems = itemsToDefer
                .OrderByDescending(x => x.priority)
                .ThenBy(x => x.isExisting)
                .ToList();

            // defer items in priority order
            foreach (var (element, priority, isExisting) in sortedItems)
            {
                if (MoveCancellationRequested) 
                {
                    break;
                }

                try
                {
                    await ClickElement(element);
                    
                    // track deferred item for potential auto pickup
                    _deferredItems.Add(element);
                    
                    await Task.Delay(Settings.ActionDelay + RandomDelay());
                }
                catch (Exception ex)
                {
                    LogError($"error clicking item with priority {priority}: {ex.Message}");
                }
            }
        }

        private string GetItemMatchName(Entity itemEntity, string fallbackName)
        {
            try
            {
                if (itemEntity.TryGetComponent<Mods>(out var mods))
                {
                    var uniqueName = mods.UniqueName?.Replace('\x2019', '\x27');
                    if (!string.IsNullOrWhiteSpace(uniqueName))
                    {
                        return uniqueName;
                    }

                    if (mods.ItemRarity == ItemRarity.Unique && !mods.Identified)
                    {
                        var artPath = itemEntity.GetComponent<RenderItem>()?.ResourcePath;
                        if (!string.IsNullOrWhiteSpace(artPath))
                        {
                            var mapping = GetUniqueArtMapping();
                            var candidate = mapping.GetValueOrDefault(artPath)?.FirstOrDefault();
                            if (!string.IsNullOrWhiteSpace(candidate))
                            {
                                return candidate.Replace('\x2019', '\x27');
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return fallbackName;
        }

        private Dictionary<string, List<string>> GetUniqueArtMapping()
        {
            if (_uniqueArtMapping != null)
            {
                return _uniqueArtMapping;
            }

            try
            {
                GameController.Files.UniqueItemDescriptions.ReloadIfEmptyOrZero();

                _uniqueArtMapping = GameController.Files.ItemVisualIdentities.EntriesList
                    .Where(identity => identity.ArtPath != null)
                    .GroupJoin(
                        GameController.Files.UniqueItemDescriptions.EntriesList.Where(description => description.ItemVisualIdentity != null),
                        identity => identity,
                        description => description.ItemVisualIdentity,
                        (identity, descriptions) => new { identity.ArtPath, Descriptions = descriptions.ToList() })
                    .GroupBy(entry => entry.ArtPath, entry => entry.Descriptions)
                    .Select(group => new
                    {
                        ArtPath = group.Key,
                        Names = group.SelectMany(items => items)
                            .Select(item => item.UniqueName?.Text)
                            .Where(name => !string.IsNullOrWhiteSpace(name))
                            .Select(name => name!)
                            .Distinct()
                            .ToList()
                    })
                    .Where(entry => entry.Names.Any())
                    .ToDictionary(entry => entry.ArtPath, entry => entry.Names);
            }
            catch (Exception ex)
            {
                LogError($"failed to build unique art mapping: {ex.Message}");
                _uniqueArtMapping = new Dictionary<string, List<string>>();
            }

            return _uniqueArtMapping;
        }

        private bool EnsureApiServiceAvailable()
        {
            if (_getNinjaEntityValue == null || _getNinjaBaseItemTypeValue == null)
            {
                if (!_hasLoggedMissingPluginBridge)
                {
                    LogError("NinjaPricer PluginBridge methods are unavailable");
                    _hasLoggedMissingPluginBridge = true;
                }

                return false;
            }

            _hasLoggedMissingPluginBridge = false;

            if (_apiService != null)
            {
                return true;
            }

            try
            {
                _apiService = new NinjaPricerBridgeService(
                    entity => _getNinjaEntityValue?.Invoke(entity),
                    baseItemType => _getNinjaBaseItemTypeValue?.Invoke(baseItemType),
                    FindBaseItemTypeByName,
                    null,
                    msg => LogError($"NinjaPricer: {msg}")
                );
                return true;
            }
            catch (Exception ex)
            {
                LogError($"failed to initialize NinjaPricer service: {ex.Message}");
                return false;
            }
        }

        private BaseItemType? FindBaseItemTypeByName(string baseName)
        {
            if (string.IsNullOrWhiteSpace(baseName))
            {
                return null;
            }

            return GameController.Files.BaseItemTypes.Contents.Values
                .FirstOrDefault(item => item != null &&
                                        string.Equals(item.BaseName, baseName, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsButtonPressed(RectangleF buttonRect)
        {
            var prevState = _mouseStateForRect.GetValueOrDefault(buttonRect);
            var cursorPos = Input.MousePosition;
            var windowOffset = GameController.Window.GetWindowRectangleTimeCache.TopLeft;
            var relativeCursorPos = new Vector2(cursorPos.X - windowOffset.X, cursorPos.Y - windowOffset.Y);
            
            var isHovered = buttonRect.Contains(relativeCursorPos);
            if (!isHovered)
            {
                _mouseStateForRect[buttonRect] = null;
                return false;
            }

            var isPressed = Control.MouseButtons == MouseButtons.Left;
            _mouseStateForRect[buttonRect] = isPressed;
            
            // button press detected on transition from not pressed to pressed
            return isPressed && prevState == false;
        }

        private void EnsureDeferItemsLoaded()
        {
            if (Settings?.DeferGroup?.Items == null) return;

            foreach (var item in Settings.DeferGroup.Items)
            {
                if (item != null)
                {
                    // ensure IsApiItem ToggleNode is initialized
                    if (item.IsApiItem == null)
                    {
                        item.IsApiItem = new ExileCore2.Shared.Nodes.ToggleNode(false);
                    }
                }
            }
        }

        private async Task ClickElement(Element element)
        {
            if (element == null)
            {
                LogError("attempted to click null element");
                return;
            }
            
            try
            {
                // capture original mouse position on first click
                if (!_originalMousePosition.HasValue)
                {
                    _originalMousePosition = Input.MousePosition;
                }

                var position = element.GetClientRectCache.Center + 
                              GameController.Window.GetWindowRectangleTimeCache.TopLeft;
                Input.SetCursorPos(position);
                await Task.Delay(Settings.ActionDelay + RandomDelay());
                Input.Click(MouseButtons.Left);
                await Task.Delay(Settings.ActionDelay + RandomDelay());
            }
            catch (Exception ex)
            {
                LogError($"error clicking element: {ex.Message}");
            }
        }

        private async Task CtrlClickElement(Element element)
        {
            if (element == null)
            {
                LogError("attempted to ctrl+click null element");
                return;
            }
            
            try
            {
                var position = element.GetClientRectCache.Center + 
                              GameController.Window.GetWindowRectangleTimeCache.TopLeft;
                Input.SetCursorPos(position);
                await Task.Delay(Settings.ActionDelay + RandomDelay());
                
                // hold ctrl and click
                Input.KeyDown(Keys.LControlKey);
                await Task.Delay(Settings.ActionDelay + RandomDelay());
                Input.Click(MouseButtons.Left);
                await Task.Delay(Settings.ActionDelay + RandomDelay());
                Input.KeyUp(Keys.LControlKey);
            }
            catch (Exception ex)
            {
                LogError($"error ctrl+clicking element: {ex.Message}");
            }
        }

        private async Task AutoConfirm()
        {
            try
            {
                // wait a moment for the UI to update after deferring
                await Task.Delay(Settings.ActionDelay + RandomDelay());
                
                var confirmElement = GetConfirmElement();
                if (confirmElement != null)
                {
                    await ClickElement(confirmElement);
                    await Task.Delay(Settings.ActionDelay + RandomDelay());
                }
            }
            catch (Exception ex)
            {
                LogError($"error during auto confirm: {ex.Message}");
            }
        }

        private async Task AutoPickup()
        {
            try
            {
                if (_deferredItems.Count == 0)
                {
                    return;
                }
                
                // wait a moment for the UI to update after confirming
                await Task.Delay(Settings.ActionDelay + RandomDelay());
                
                var successfulPickups = 0;
                
                foreach (var item in _deferredItems)
                {
                    if (MoveCancellationRequested)
                    {
                        break;
                    }
                    
                    try
                    {
                        await CtrlClickElement(item);
                        successfulPickups++;
                        await Task.Delay(Settings.ActionDelay + RandomDelay());
                    }
                    catch (Exception ex)
                    {
                        LogError($"error during auto pickup of item: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"error during auto pickup: {ex.Message}");
            }
        }

        private async Task AutoReroll()
        {
            try
            {
                // wait a moment for the UI to update
                await Task.Delay(Settings.ActionDelay + RandomDelay());
                
                var rerollElement = GetRerollElement();
                if (rerollElement != null)
                {
                    await ClickElement(rerollElement);
                    await Task.Delay(Settings.ActionDelay + RandomDelay());
                }
            }
            catch (Exception ex)
            {
                LogError($"error during auto reroll: {ex.Message}");
            }
        }

        private async Task RestoreMousePosition()
        {
            if (_originalMousePosition.HasValue)
            {
                try
                {
                    Input.SetCursorPos(_originalMousePosition.Value);
                    _originalMousePosition = null; // reset for next operation
                    await Task.Delay(Settings.ActionDelay + RandomDelay());
                }
                catch (Exception ex)
                {
                    LogError($"error restoring mouse position: {ex.Message}");
                }
            }
        }

        public override void OnPluginDestroyForHotReload()
        {
            base.OnPluginDestroyForHotReload();
            _apiService?.Dispose();
        }
    }
}
