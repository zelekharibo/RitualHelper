using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Text.Json.Serialization;
using ImGuiNET;
using RitualHelper.Utils;

namespace RitualHelper
{
    public class DeferGroup
    {
        [JsonIgnore]
        private bool _expand;
        
        [JsonIgnore]
        private int _deleteIndex = -1;

        [JsonPropertyName("items")]
        public List<DeferItem> Items { get; set; } = new();
        
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;
        
        [JsonPropertyName("name")]
        public string Name { get; set; } = "Default Defer Group";

        public DeferGroup()
        {
        }

        public DeferGroup(string name = "New Defer Group")
        {
            Name = name;
        }

        public void DrawSettings()
        {
            DrawGroupHeader();
            DrawGroupControls();
            DrawItemList();
            DrawAddItemButton();
            HandleDeleteConfirmation();
        }

        private void DrawGroupHeader()
        {
            var enabled = Enabled;
            var name = Name;
            
            // group enable toggle with color
            if (enabled) ImGui.PushStyleColor(ImGuiCol.Text, Color.Lime.ToVector4());
            if (ImGui.Checkbox("Enable Group", ref enabled)) Enabled = enabled;
            if (enabled) ImGui.PopStyleColor();

            // group name input
            ImGui.SameLine();
            if (ImGui.InputText("Group Name", ref name, 50)) Name = name;
        }

        private void DrawGroupControls()
        {
            // expand/collapse button
            if (Items.Any())
            {
                if (!_expand) ImGui.PushStyleColor(ImGuiCol.Button, Color.Green.ToVector4());
                if (ImGui.Button($"{(_expand ? "Collapse" : "Expand")}###ExpandHideButton"))
                {
                    _expand = !_expand;
                }
                if (!_expand) ImGui.PopStyleColor();

                ImGui.SameLine();
            }

            // sort button
            if (ImGui.Button("Sort by Priority"))
            {
                SortItemsByPriority();
            }
            
            ImGui.SameLine();
            
            // clear all button
            if (Items.Any())
            {
                ImGui.PushStyleColor(ImGuiCol.Button, Color.Red.ToVector4());
                if (ImGui.Button("Clear All"))
                {
                    if (ImGui.IsKeyDown(ImGuiKey.ModShift))
                    {
                        ClearAllItems();
                    }
                    else
                    {
                        _deleteIndex = -2; // special value to indicate clear all
                    }
                }
                ImGui.PopStyleColor();
                
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("hold Shift to clear all without confirmation");
                }
            }
        }

        private void DrawItemList()
        {
            // create a copy of the items list to avoid concurrent modification issues
            var itemsCopy = Items?.ToList() ?? new List<DeferItem>();
            
            for (var i = 0; i < itemsCopy.Count; i++)
            {
                // double-check that the item still exists in the original list
                if (i >= Items.Count || Items[i] != itemsCopy[i])
                {
                    break; // list was modified, stop rendering
                }
                
                ImGui.PushID($"DeferItem{i}");
                
                try
                {
                    if (i != 0)
                    {
                        ImGui.Separator();
                    }

                    DrawItemControls(i);
                    
                    // additional safety check before accessing Items[i]
                    if (i < Items.Count && Items[i] != null)
                    {
                        Items[i].Display(_expand);
                    }
                }
                finally
                {
                    ImGui.PopID();
                }
            }
        }

        private void DrawItemControls(int index)
        {
            // safety check
            if (index < 0 || index >= Items.Count) return;
            
            // move up button
            if (index > 0 && index < Items.Count && ImGui.SmallButton("^"))
            {
                MoveItem(index, index - 1);
            }
            if (index > 0) ImGui.SameLine();

            // move down button
            if (index < Items.Count - 1 && index >= 0 && ImGui.SmallButton("v"))
            {
                MoveItem(index, index + 1);
            }
            if (index < Items.Count - 1) ImGui.SameLine();

            // delete button
            if (ImGui.Button("Delete"))
            {
                if (ImGui.IsKeyDown(ImGuiKey.ModShift))
                {
                    RemoveAt(index);
                    return;
                }

                _deleteIndex = index;
            }
            else if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("hold Shift to delete without confirmation");
            }

            ImGui.SameLine();
        }

        private void DrawAddItemButton()
        {
            if (ImGui.Button("Add New Item"))
            {
                Items.Add(new DeferItem("New Item", 5));
            }
        }

        private void HandleDeleteConfirmation()
        {
            if (_deleteIndex != -1)
            {
                ImGui.OpenPopup("DeferItemDeleteConfirmation");
            }

            string? itemName = null;
            if (_deleteIndex == -2)
            {
                itemName = $"ALL {Items.Count} defer items";
            }
            else if (_deleteIndex >= 0 && _deleteIndex < Items.Count)
            {
                itemName = $"defer item '{Items[_deleteIndex].Name}'";
            }

            var deleteResult = ImGuiExt.DrawDeleteConfirmationPopup(
                "DeferItemDeleteConfirmation", itemName);
                
            if (deleteResult == true)
            {
                if (_deleteIndex == -2)
                {
                    ClearAllItems();
                }
                else
                {
                    RemoveAt(_deleteIndex);
                }
            }

            if (deleteResult.HasValue)
            {
                _deleteIndex = -1;
            }
        }

        private void SortItemsByPriority()
        {
            Items = Items.OrderByDescending(x => x.Priority).ThenBy(x => x.Name).ToList();
        }

        private void ClearAllItems()
        {
            if (Items != null)
            {
                try
                {
                    Items.Clear();
                }
                catch (Exception)
                {
                    // in case of concurrent modification, create a new list
                    Items = new List<DeferItem>();
                }
            }
        }

        public List<DeferItem> GetActiveItems()
        {
            if (!Enabled) return new List<DeferItem>();

            return Items.Where(x => x.Enabled)
                       .OrderByDescending(x => x.Priority)
                       .ThenBy(x => x.Name)
                       .ToList();
        }

        private void RemoveAt(int index)
        {
            if (Items == null) return;
            
            if (index >= 0 && index < Items.Count)
            {
                try
                {
                    Items.RemoveAt(index);
                }
                catch (ArgumentOutOfRangeException)
                {
                    // list was modified concurrently, ignore
                }
            }
        }

        private void MoveItem(int sourceIndex, int targetIndex)
        {
            if (Items == null || 
                sourceIndex < 0 || sourceIndex >= Items.Count ||
                targetIndex < 0 || targetIndex >= Items.Count ||
                sourceIndex == targetIndex)
            {
                return;
            }

            try
            {
                var movedItem = Items[sourceIndex];
                Items.RemoveAt(sourceIndex);
                Items.Insert(targetIndex, movedItem);
            }
            catch (ArgumentOutOfRangeException)
            {
                // list was modified concurrently, ignore
            }
        }

    }
}
