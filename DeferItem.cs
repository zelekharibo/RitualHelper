using System;
using System.Drawing;
using System.Numerics;
using System.Text.Json.Serialization;
using ExileCore2.Shared.Nodes;
using ImGuiNET;
using RitualHelper.Utils;

namespace RitualHelper
{
    public class DeferItem
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
        
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;
        
        [JsonPropertyName("priority")]
        public int Priority { get; set; } = 1;
        
        [JsonPropertyName("minStackSize")]
        public int MinStackSize { get; set; } = 1;

        public ToggleNode IsApiItem { get; set; } = new(false);
        
        [JsonIgnore]
        private bool _expand;

        public DeferItem()
        {
            IsApiItem = new ToggleNode(false);
        }

        public DeferItem(string name, int priority = 1, int minStackSize = 1, bool isFromApi = false)
        {
            Name = name;
            Priority = priority;
            MinStackSize = minStackSize;
            IsApiItem = new ToggleNode(isFromApi);
        }

        public void Display(bool expanded)
        {
            var enabled = Enabled;
            var name = Name;
            var priority = Priority;
            var minStackSize = MinStackSize;
            
            DrawEnabledCheckbox(ref enabled);
            DrawPriorityIndicator(priority);
            DrawStackSizeIndicator(minStackSize);
            DrawItemName(name);
            
            if (expanded || _expand)
            {
                DrawExpandedControls(ref name, ref priority, ref minStackSize);
            }
            
            if (!expanded)
            {
                DrawExpandCollapseButton();
            }
        }
        
        private void DrawEnabledCheckbox(ref bool enabled)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, enabled ? Color.Lime.ToVector4() : Color.Gray.ToVector4());
            
            if (ImGui.Checkbox("##enabled", ref enabled))
            {
                Enabled = enabled;
            }
            
            ImGui.PopStyleColor();
            ImGui.SameLine();
        }
        
        private void DrawPriorityIndicator(int priority)
        {
            var priorityColor = priority switch
            {
                >= 9 => Color.Red,
                >= 7 => Color.Orange,
                >= 5 => Color.Yellow,
                >= 3 => Color.LightGreen,
                _ => Color.LightBlue
            };
            
            ImGui.TextColored(priorityColor.ToVector4(), $"[{priority}]");
            ImGui.SameLine();
        }
        
        private void DrawStackSizeIndicator(int minStackSize)
        {
            if (minStackSize > 1)
            {
                ImGui.TextColored(Color.Cyan.ToVector4(), $"({minStackSize}+)");
                ImGui.SameLine();
            }
        }
        
        private void DrawItemName(string name)
        {
            if (IsApiItem?.Value == true)
            {
                ImGui.TextColored(Color.LightBlue.ToVector4(), "[API]");
                ImGui.SameLine();
            }
            ImGui.Text(name);
        }
        
        private void DrawExpandedControls(ref string name, ref int priority, ref int minStackSize)
        {
            ImGui.Indent();
            
            // name editing
            if (ImGui.InputText("Name", ref name, 100))
            {
                Name = name;
            }
            
            // priority slider
            if (ImGui.SliderInt("Priority", ref priority, 1, 10))
            {
                Priority = priority;
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("higher priority items are deferred first (1=lowest, 10=highest)");
            }
            
            // min stack size input
            if (ImGui.InputInt("Min Stack Size", ref minStackSize))
            {
                MinStackSize = Math.Max(1, minStackSize);
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("only defer if stack size is at least this amount");
            }
            
            // API item toggle
            var isApiValue = IsApiItem.Value;
            if (ImGui.Checkbox("From API", ref isApiValue))
            {
                IsApiItem.Value = isApiValue;
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("indicates if this item was added from API data");
            }
            
            ImGui.Unindent();
        }
        
        private void DrawExpandCollapseButton()
        {
            ImGui.SameLine();
            if (ImGui.SmallButton(_expand ? "Less" : "More"))
            {
                _expand = !_expand;
            }
        }

        public bool ShouldDefer(string itemBaseName, int stackSize = 1)
        {
            if (!Enabled) return false;
            if (stackSize < MinStackSize) return false;
            if (string.IsNullOrEmpty(Name)) return false;
            
            return itemBaseName.Contains(Name, StringComparison.OrdinalIgnoreCase);
        }
    }
}
