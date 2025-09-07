using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExileCore2;
using ExileCore2.Shared;
using ExileCore2.PoEMemory;
using ExileCore2.PoEMemory.Components;
using ExileCore2.PoEMemory.Elements;
using ExileCore2.PoEMemory.Elements.InventoryElements;
using ExileCore2.PoEMemory.MemoryObjects;
using GameOffsets2.Native;
using ImGuiNET;
using RitualHelper.Utils;

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
        private Vector2i? _originalMousePosition;
        private bool _isApiFetching = false;
        private string? _lastUsedLeagueName;
        private bool _lastUsedNinjaPricerData;

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


        private Element GetTributeElement()
        {
            var ritualWindow = GameController.IngameState.IngameUi.RitualWindow;
            if (ritualWindow?.Children == null) return null;

            foreach (var element in ritualWindow.Children)
            {
                if (element?.Children == null) continue;
                
                foreach (var subElement in element.Children)
                {
                    var text = subElement?.Text;
                    if (!string.IsNullOrEmpty(text) && text.Contains("Tribute Remaining"))
                    {
                        return subElement;
                    }
                }
            }

            return null;
        }

        private Element GetDeferringElement()
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


        private Element GetCancelElement()
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
            }
            catch (Exception ex)
            {
                LogError($"error during defer process: {ex.Message}");
            }
            finally
            {
                // restore mouse to original position after all work is done
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
                            itemsToDefer.Add((element, matchingItem.Priority, isAlreadyDeferred));
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
            var cursorPos = Mouse.GetCursorPosition();
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
                    _originalMousePosition = Mouse.GetCursorPosition();
                }

                var position = element.GetClientRectCache.Center + 
                              GameController.Window.GetWindowRectangleTimeCache.TopLeft;
                await Mouse.MoveMouse(position);
                await Task.Delay(RandomDelay());
                await Mouse.LeftDown();
                await Task.Delay(RandomDelay());
                await Mouse.LeftUp();
            }
            catch (Exception ex)
            {
                LogError($"error clicking element: {ex.Message}");
            }
        }

        private async Task RestoreMousePosition()
        {
            if (_originalMousePosition.HasValue)
            {
                try
                {
                    await Mouse.MoveMouse(_originalMousePosition.Value);
                    _originalMousePosition = null; // reset for next operation
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
                    
                    foreach (var item in currentItems)
                    {
                        if (IsManualItem(item, apiItemNames))
                        {
                            manualItems.Add(item);
                        }
                        else
                        {
                            removedApiItems.Add(item);
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