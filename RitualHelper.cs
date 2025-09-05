using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExileCore2;
using ExileCore2.PoEMemory;
using ExileCore2.PoEMemory.Components;
using ExileCore2.PoEMemory.Elements;
using ExileCore2.PoEMemory.Elements.InventoryElements;
using ExileCore2.PoEMemory.MemoryObjects;
using ExileCore2.Shared;
using ExileCore2.Shared.Enums;
using RitualHelper.Utils;
using ImGuiNET;
using System.Text.RegularExpressions;

namespace RitualHelper
{
    
    public class RitualHelper : BaseSettingsPlugin<Settings>
    {
        private readonly ConcurrentDictionary<RectangleF, bool?> _mouseStateForRect = new();

        private bool MoveCancellationRequested => Settings.CancelWithRightClick && (Control.MouseButtons & MouseButtons.Right) != 0;

        public override bool Initialise()
        {
            Graphics.InitImage(Path.Combine(DirectoryFullName, "images\\pick.png").Replace('\\', '/'), false);
            return true;
        }

        public override void Render()
        {
            if (!Settings.Enable) return;

            var ritualPanel = GameController.IngameState.IngameUi.RitualWindow;
            if (ritualPanel != null && ritualPanel.IsVisible)
            {

                // find defer button or cancel button
                var buttonToUse = GetDeferringElement() != null ? GetDeferringElement() : GetCancelElement();
                if (buttonToUse == null) {
                    return;
                }

                const float offset = 20;
                const float buttonSize = 37;
                var buttonPos = buttonToUse.GetClientRectCache.TopRight + GameController.Window.GetWindowRectangleTimeCache.TopLeft;
                var buttonRect = new RectangleF(buttonPos.X + buttonSize + offset, buttonPos.Y, buttonSize, buttonSize);
                Graphics.DrawImage("pick.png", buttonRect);

                if (IsButtonPressed(buttonRect))
                {
                    _ = Task.Run(async () =>
                    {
                        while (Control.MouseButtons == MouseButtons.Left)
                        {
                            await Task.Delay(10);
                        }
                    });
                    StartDefer();
                }
            }
        }

        private int RandomDelay() {
            return new Random().Next(Settings.RandomDelay);
        }

        private Element GetTributeElement() {
            foreach (var element in GameController.IngameState.IngameUi.RitualWindow.Children ?? Enumerable.Empty<Element>()) {
                foreach (var subElement in element.Children ?? Enumerable.Empty<Element>()) {
                    var text = subElement.Text;
                    if (!string.IsNullOrEmpty(text) && text.Contains("Tribute Remaining")) {
                        return subElement;
                    }
                }
            }

            return null;
        }


        private Element GetDeferringElement() {
            foreach (var element in GameController.IngameState.IngameUi.RitualWindow.Children ?? Enumerable.Empty<Element>()) {
                foreach (var subElement in element.Children ?? Enumerable.Empty<Element>()) {
                    if (subElement.Text == "defer item") {
                        return subElement;
                    }
                }
            }

            return null;
        }

        private Element GetCancelElement() {
            foreach (var element in GameController.IngameState.IngameUi.RitualWindow.Children ?? Enumerable.Empty<Element>()) {
                foreach (var subElement in element.Children ?? Enumerable.Empty<Element>()) {
                    if (subElement.Text == "cancel") {
                        return subElement;
                    }
                }
            }

            return null;
        }

        private int GetRemainingTribute() {
            var tributeElement = GetTributeElement();
            if (tributeElement != null) {
                var text = tributeElement.Text;
                if (!string.IsNullOrEmpty(text) && text.Contains("Tribute Remaining")) {
                    // remove spaces and extract digits
                    var digits = Regex.Replace(text, @"\D", "");
                    if (!string.IsNullOrEmpty(digits) && int.TryParse(digits, out var tribute)) {
                        return tribute;
                    }
                }
            }

            return 0;
        }

        private bool IsDeferringEnabled() {
            var deferringElement = GetDeferringElement();
            if (deferringElement == null) {
                return true;
            }

            return false;
        }
    
        private async Task ToggleDeferring(bool enable) {
            if (enable && IsDeferringEnabled() || !enable && !IsDeferringEnabled()) {
                return;
            }

            var deferringElement = GetDeferringElement();
            if (enable && deferringElement != null) {
                var position = deferringElement.GetClientRectCache.Center + GameController.Window.GetWindowRectangleTimeCache.TopLeft;
                Mouse.MoveMouse(position);
                Mouse.LeftDown();
                Mouse.LeftUp();

                await Task.Delay(Settings.ActionDelay + RandomDelay());
                if (IsDeferringEnabled()) {
                    return;
                }
                await ToggleDeferring(true);
                return;
            }

            var cancelElement = GetCancelElement();
            if (!enable && cancelElement != null) {
                var position = cancelElement.GetClientRectCache.Center + GameController.Window.GetWindowRectangleTimeCache.TopLeft;
                Mouse.MoveMouse(position);
                Mouse.LeftDown();
                Mouse.LeftUp();

                await Task.Delay(Settings.ActionDelay + RandomDelay());
                if (!IsDeferringEnabled()) {
                    return;
                }
                await ToggleDeferring(false);
                return;
            }
        }

        private async Task DeferExistingItems() {
            foreach (var element in GameController.IngameState.IngameUi.RitualWindow.Items) {
                if (element.Children.Count >= 3) {
                    var pos = element.GetClientRectCache.Center + GameController.Window.GetWindowRectangleTimeCache.TopLeft;
                    Mouse.MoveMouse(pos);
                    await Task.Delay(RandomDelay());
                    Mouse.LeftDown();
                    await Task.Delay(RandomDelay());
                    Mouse.LeftUp();
                    await Task.Delay(Settings.ActionDelay + RandomDelay());
                }
            }
        }

        private List<string> GetDeferNewItemsList() {
            return Settings.DeferNewItemsList.Value.Split(',').Select(item => item.Trim()).ToList();
        }

        private async Task DeferNewItems() {
            var deferNewItemsList = GetDeferNewItemsList();
            foreach (var element in GameController.IngameState.IngameUi.RitualWindow.Items) {
                var baseItemType = GameController.Files.BaseItemTypes.Translate(element.Item.Metadata);
                if (element.Children.Count < 3 && deferNewItemsList.Any(defer => baseItemType.BaseName.Contains(defer))) {
                    var pos = element.GetClientRectCache.Center + GameController.Window.GetWindowRectangleTimeCache.TopLeft;
                    Mouse.MoveMouse(pos);
                    await Task.Delay(RandomDelay());
                    Mouse.LeftDown();
                    await Task.Delay(RandomDelay());
                    Mouse.LeftUp();
                    await Task.Delay(Settings.ActionDelay + RandomDelay());
                }
            }
        }

        private async void StartDefer()
        {
            if (Settings.DeferExistingItems || Settings.DeferNewItems) {
                await ToggleDeferring(true);
                await Task.Delay(Settings.ActionDelay + RandomDelay());
            }
           
            if (Settings.DeferExistingItems) {
                await DeferExistingItems();
                await Task.Delay(Settings.ActionDelay + RandomDelay());
            }

            if (Settings.DeferNewItems) {
                await DeferNewItems();
                await Task.Delay(Settings.ActionDelay + RandomDelay());
            }
        }

        private bool IsButtonPressed(RectangleF buttonRect)
        {
            var prevState = _mouseStateForRect.GetValueOrDefault(buttonRect);
            var cursorPos = Mouse.GetCursorPosition();
            var windowOffset = GameController.Window.GetWindowRectangleTimeCache.TopLeft;
            var relativeCursorPos = new Vector2(cursorPos.X - windowOffset.X, cursorPos.Y - windowOffset.Y);
            var isHovered = buttonRect.Contains(relativeCursorPos);
            if (!isHovered) {
                _mouseStateForRect[buttonRect] = null;
                return false;
            }

            var isPressed = Control.MouseButtons == MouseButtons.Left;
            _mouseStateForRect[buttonRect] = isPressed;
            return isPressed && prevState == false;
        }
    }
}