using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExileCore2;
using ExileCore2.Shared;
using ExileCore2.PoEMemory;
using ExileCore2.PoEMemory.Components;
using ImGuiNET;

namespace RitualHelper
{
    public class RitualHelper : BaseSettingsPlugin<Settings>
    {
        private const float ButtonOffset = 20f;
        private const float ButtonSize = 37f;
        private const string ImageName = "pick.png";
        
        private static readonly Random StaticRandom = new();
        private readonly ConcurrentDictionary<RectangleF, bool?> _mouseStateForRect = new();
        private PoE2ScoutApiService? _apiService;
        private DateTime _lastApiUpdate = DateTime.MinValue;
        private Vector2? _originalMousePosition;
        private bool _isApiFetching = false;
        private string? _lastUsedLeagueName;
        private bool _lastUsedNinjaPricerData;
        private readonly List<Element> _deferredItems = new();

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
                
                LogMessage("RitualHelper initialized successfully");
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
            base.DrawSettings();
            
            ImGui.Separator();
            ImGui.Text("API Integration");
            
            // api integration controls
            if (Settings.EnableApiIntegration.Value)
            {
                if (ImGui.Button(_isApiFetching ? "Updating..." : "Update from API Now"))
                {
                    if (!_isApiFetching)
                    {
                        _ = Task.Run(UpdateDeferListFromApiSafe);
                    }
                }
                
                ImGui.SameLine();
                var lastUpdateText = _lastApiUpdate == DateTime.MinValue 
                    ? "Never updated" 
                    : $"Last updated: {_lastApiUpdate:HH:mm:ss}";
                ImGui.Text(lastUpdateText);
                
            }
            
            ImGui.Separator();
            ImGui.Text("Defer Items");
            
            // defer group settings
            Settings.DeferGroup.DrawSettings();
        }


        public override void Render()
        {
            if (!Settings.Enable) return;

            // check if we need to update from API (but avoid concurrent fetches)
            if (Settings.EnableApiIntegration.Value && ShouldUpdateFromApi() && !_isApiFetching)
            {
                _ = Task.Run(UpdateDeferListFromApiSafe);
            }

            var ritualPanel = GameController.IngameState.IngameUi.RitualWindow;
            if (ritualPanel?.IsVisible != true) return;


            // find defer button or cancel button
            var buttonToUse = GetDeferringElement() ?? GetCancelElement();
            if (buttonToUse == null) return;

            var buttonPos = buttonToUse.GetClientRectCache.TopRight + 
                           GameController.Window.GetWindowRectangleTimeCache.TopLeft;
            var buttonRect = new RectangleF(
                buttonPos.X + ButtonSize + ButtonOffset, 
                buttonPos.Y, 
                ButtonSize, 
                ButtonSize);
            
            Graphics.DrawImage(ImageName, buttonRect);

            if (IsButtonPressed(buttonRect))
            {
                _ = Task.Run(async () =>
                {
                    // wait for mouse release before proceeding
                    while (Control.MouseButtons == MouseButtons.Left)
                    {
                        await Task.Delay(10);
                    }
                });
                StartDefer();
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



        private bool IsDeferringEnabled()
        {
            var deferringElement = GetDeferringElement();
            return deferringElement == null;
        }

        private async Task ToggleDeferring(bool enable)
        {
            // check if already in desired state
            if ((enable && IsDeferringEnabled()) || (!enable && !IsDeferringEnabled()))
            {
                return;
            }

            if (enable)
            {
                var deferringElement = GetDeferringElement();
                if (deferringElement != null)
                {
                    await ClickElement(deferringElement);
                    await Task.Delay(Settings.ActionDelay + RandomDelay());
                    
                    // verify state change and retry if needed
                    if (!IsDeferringEnabled())
                    {
                        await ToggleDeferring(true);
                    }
                }
            }
            else
            {
                var cancelElement = GetCancelElement();
                if (cancelElement != null)
                {
                    await ClickElement(cancelElement);
                    await Task.Delay(Settings.ActionDelay + RandomDelay());
                    
                    // verify state change and retry if needed
                    if (IsDeferringEnabled())
                    {
                        await ToggleDeferring(false);
                    }
                }
            }
        }

        private async void StartDefer()
        {
            try
            {
                // clear previously deferred items list
                _deferredItems.Clear();
                
                var hasActiveItems = Settings.DeferGroup.GetActiveItems().Any();
                
                if (Settings.DeferExistingItems || hasActiveItems)
                {
                    await ToggleDeferring(true);
                    await Task.Delay(Settings.ActionDelay + RandomDelay());
                }

                if (hasActiveItems)
                {
                    await DeferItemsByPriority();
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
            }
        }

        private async Task DeferItemsByPriority()
        {
            var activeItems = Settings.DeferGroup.GetActiveItems();
            if (!activeItems.Any()) return;

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
                    var stackSize = element.Item.GetComponent<Stack>()?.Size ?? 1;
                    var matchingItem = activeItems.FirstOrDefault(item => item.ShouldDefer(baseItemType.BaseName, stackSize));

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
                    LogMessage("defer operation cancelled by user (right-click)");
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

        private bool ShouldUpdateFromApi()
        {
            if (!Settings.EnableApiIntegration.Value) return false;
            
            // try to get last update time from persistent settings
            var lastUpdate = GetLastApiUpdateFromSettings();
            
            var updateInterval = TimeSpan.FromMinutes(Settings.ApiUpdateInterval.Value);
            return DateTime.Now - lastUpdate >= updateInterval;
        }

        private DateTime GetLastApiUpdateFromSettings()
        {
            if (string.IsNullOrEmpty(Settings.LastApiUpdateTime))
            {
                // if this is the first time ever, set a recent time to prevent immediate update
                var firstTime = DateTime.Now.AddMinutes(-Settings.ApiUpdateInterval.Value + 5); // wait 5 more minutes
                return firstTime;
            }
            
            if (DateTime.TryParse(Settings.LastApiUpdateTime, out var lastUpdate))
            {
                _lastApiUpdate = lastUpdate; // sync the in-memory value
                return lastUpdate;
            }
            
            return DateTime.MinValue;
        }

        private void SaveLastApiUpdateTime()
        {
            _lastApiUpdate = DateTime.Now;
            Settings.LastApiUpdateTime = _lastApiUpdate.ToString("O"); // use ISO 8601 format
        }

        private void EnsureDeferItemsLoaded()
        {
            if (Settings?.DeferGroup?.Items == null) return;
            
            LogMessage($"Loading {Settings.DeferGroup.Items.Count} defer items into memory...");
            
            var itemsNeedingInit = 0;
            foreach (var item in Settings.DeferGroup.Items)
            {
                if (item != null)
                {
                    // ensure IsApiItem ToggleNode is initialized
                    if (item.IsApiItem == null)
                    {
                        item.IsApiItem = new ExileCore2.Shared.Nodes.ToggleNode(false);
                        itemsNeedingInit++;
                    }
                    
                    LogMessage($"  Item: '{item.Name}' - IsApiItem: {item.IsApiItem?.Value}");
                }
            }
            
            if (itemsNeedingInit > 0)
            {
                LogMessage($"Initialized ToggleNode for {itemsNeedingInit} items");
            }
            
            LogMessage("All defer items loaded into memory");
        }

        private bool IsManualItem(DeferItem item, HashSet<string> apiItemNames)
        {
            if (item == null) return false;
            
            // primary check: use the properly serialized IsApiItem toggle node
            if (item.IsApiItem?.Value == true)
            {
                LogMessage($"    '{item.Name}' marked as API item via ToggleNode");
                return false; // this is an API item
            }
            
            // additional check: if name appears in current API results, it's likely an API item
            if (apiItemNames.Contains(item.Name))
            {
                LogMessage($"    '{item.Name}' found in current API results, treating as API item");
                return false;
            }
            
            LogMessage($"    '{item.Name}' considered manual item");
            return true;
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
                LogMessage("attempting auto confirm...");
                
                // wait a moment for the UI to update after deferring
                await Task.Delay(Settings.ActionDelay + RandomDelay());
                
                var confirmElement = GetConfirmElement();
                if (confirmElement != null)
                {
                    LogMessage("confirm element found, clicking...");
                    await ClickElement(confirmElement);
                    await Task.Delay(Settings.ActionDelay + RandomDelay());
                }
                else
                {
                    LogMessage("confirm element not found");
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
                LogMessage($"attempting auto pickup of {_deferredItems.Count} deferred items...");
                
                if (_deferredItems.Count == 0)
                {
                    LogMessage("no deferred items to pickup");
                    return;
                }
                
                // wait a moment for the UI to update after confirming
                await Task.Delay(Settings.ActionDelay + RandomDelay());
                
                var successfulPickups = 0;
                
                foreach (var item in _deferredItems)
                {
                    if (MoveCancellationRequested)
                    {
                        LogMessage("auto pickup cancelled by user (right-click)");
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
                
                LogMessage($"auto pickup completed: {successfulPickups}/{_deferredItems.Count} items picked up");
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
                LogMessage("attempting auto reroll...");
                
                // wait a moment for the UI to update
                await Task.Delay(Settings.ActionDelay + RandomDelay());
                
                var rerollElement = GetRerollElement();
                if (rerollElement != null)
                {
                    LogMessage("reroll element found, clicking...");
                    await ClickElement(rerollElement);
                    await Task.Delay(Settings.ActionDelay + RandomDelay());
                }
                else
                {
                    LogMessage("reroll element not found");
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

        private async Task UpdateDeferListFromApiSafe()
        {
            // prevent concurrent API fetches
            if (_isApiFetching)
            {
                LogMessage("API fetch already in progress, skipping duplicate request");
                return;
            }

            _isApiFetching = true;
            try
            {
                await UpdateDeferListFromApi();
            }
            finally
            {
                _isApiFetching = false;
            }
        }

        private async Task UpdateDeferListFromApi()
        {
            try
            {
                LogMessage("Starting API update process...");
                
                // validate settings
                if (!ValidateApiSettings()) return;
                
                LogMessage($"settings validated - League: {Settings.LeagueName.Value}, MinValue: {Settings.MinExaltedValue.Value}");
                
                // initialize or recreate API service if needed (recreate if settings changed)
                if (_apiService == null || ShouldRecreateApiService())
                {
                    _apiService?.Dispose();
                    _apiService = new PoE2ScoutApiService(
                        Settings.LeagueName.Value,
                        msg => LogMessage($"API: {msg}"),
                        msg => LogError($"API: {msg}"),
                        Settings.UseNinjaPricerData.Value
                    );
                    
                    // update last used settings
                    _lastUsedLeagueName = Settings.LeagueName.Value;
                    _lastUsedNinjaPricerData = Settings.UseNinjaPricerData.Value;
                }

                // fetch defer list from API
                var apiDeferItems = await _apiService.GenerateDeferListAsync((decimal)Settings.MinExaltedValue.Value);
                
                if (apiDeferItems?.Any() != true)
                {
                    LogMessage("No valuable items found in API data");
                    return;
                }
                
                LogMessage($"API returned {apiDeferItems.Count} items:");
                foreach (var item in apiDeferItems.Take(10)) // log first 10 items
                {
                    LogMessage($"  API Item: '{item.Name}' (Priority: {item.Priority})");
                }
                if (apiDeferItems.Count > 10)
                {
                    LogMessage($"  ... and {apiDeferItems.Count - 10} more API items");
                }
                
                // update defer list based on settings
                UpdateDeferItemList(apiDeferItems);
                
                LogMessage($"Successfully updated defer list with {apiDeferItems.Count} items from API");
            }
            catch (Exception ex)
            {
                LogError($"Failed to update defer list from API: {ex.Message}");
                if (ex.InnerException != null)
                {
                    LogError($"Inner exception: {ex.InnerException.Message}");
                }
            }
        }

        private bool ValidateApiSettings()
        {
            if (Settings?.LeagueName?.Value == null)
            {
                LogError("league name is not configured");
                return false;
            }
                
            if (Settings?.MinExaltedValue?.Value == null)
            {
                LogError("minimum exalted value is not configured");
                return false;
            }
            
            return true;
        }

        private void UpdateDeferItemList(List<DeferItem> apiDeferItems)
        {
            if (Settings?.DeferGroup?.Items == null)
            {
                LogError("DeferGroup.Items is null");
                return;
            }
            
            if (apiDeferItems?.Any() != true)
            {
                LogMessage("No API items to add");
                return;
            }
            
            try
            {
                if (Settings.ReplaceManualItems?.Value == true)
                {
                    LogMessage("Replacing all existing items with API data");
                    var newList = new List<DeferItem>(apiDeferItems);
                    Settings.DeferGroup.Items = newList;
                }
                else
                {
                    LogMessage("Merging API data with existing manual items");
                    
                    // create a thread-safe copy of the current items
                    var currentItems = Settings.DeferGroup.Items?.ToList() ?? new List<DeferItem>();
                    LogMessage($"Current items before merge: {currentItems.Count}");
                    
                    // log items with their API status for debugging
                    foreach (var item in currentItems.Take(5)) // log first 5 for debugging
                    {
                        LogMessage($"  Item: '{item.Name}' - IsApiItem: {item.IsApiItem?.Value}");
                    }
                    if (currentItems.Count > 5)
                    {
                        LogMessage($"  ... and {currentItems.Count - 5} more items");
                    }
                    
                    // create a set of API item names for efficient lookup
                    var apiItemNames = new HashSet<string>(
                        apiDeferItems.Select(item => item.Name), 
                        StringComparer.OrdinalIgnoreCase);
                    LogMessage($"API items to add: {apiItemNames.Count}");
                    
                    // preserve manual items, remove old API items
                    // use robust detection that doesn't rely on IsFromApi flag
                    LogMessage("Starting item classification:");
                    var manualItems = new List<DeferItem>();
                    var removedApiItems = new List<DeferItem>();
                    
                    // store enabled state
                    var enabledStates = new Dictionary<string, bool>();

                    foreach (var item in currentItems)
                    {
                        if (IsManualItem(item, apiItemNames))
                        {
                            manualItems.Add(item);
                            enabledStates[item.Name] = item.Enabled;
                        }
                        else
                        {
                            removedApiItems.Add(item);
                            enabledStates[item.Name] = item.Enabled;
                        }
                    }
                    
                    LogMessage($"Classification complete:");
                    LogMessage($"  Manual items to keep: {manualItems.Count}");
                    LogMessage($"  API items to remove: {removedApiItems.Count}");
                    LogMessage($"  New API items to add: {apiDeferItems.Count}");
                    
                    // create new list with manual + API items
                    var newItemList = new List<DeferItem>();
                    newItemList.AddRange(manualItems);
                    newItemList.AddRange(apiDeferItems);

                    // restore enabled state
                    foreach (var item in newItemList)
                    {
                        if (enabledStates.TryGetValue(item.Name, out var enabled))
                        {
                            item.Enabled = enabled;
                        }
                    }
                    
                    // sort by priority, then by name
                    var sortedItems = newItemList
                        .Where(x => x != null)
                        .OrderByDescending(x => x.Priority)
                        .ThenBy(x => x.Name ?? "")
                        .ToList();
                    
                    // atomically replace the items list
                    Settings.DeferGroup.Items = sortedItems;
                    
                    LogMessage($"Final item count: {Settings.DeferGroup.Items.Count}");
                }
                
                // save the update time to persistent settings
                SaveLastApiUpdateTime();
            }
            catch (Exception ex)
            {
                LogError($"Error updating defer item list: {ex.Message}");
            }
        }

        private bool ShouldRecreateApiService()
        {
            // recreate if league name or ninja pricer setting changed
            return _lastUsedLeagueName != Settings.LeagueName.Value ||
                   _lastUsedNinjaPricerData != Settings.UseNinjaPricerData.Value;
        }

        public override void OnPluginDestroyForHotReload()
        {
            base.OnPluginDestroyForHotReload();
            _apiService?.Dispose();
        }
    }
}