using System.Collections.Generic;
using ExileCore2.Shared.Attributes;
using ExileCore2.Shared.Interfaces;
using ExileCore2.Shared.Nodes;

namespace RitualHelper
{
    public class Settings : ISettings
    {
        public ToggleNode Enable { get; set; } = new(true);
        
        [Menu("Action Delay (ms)", "Delay between actions to simulate human behavior")]
        public RangeNode<int> ActionDelay { get; set; } = new(75, 10, 1000);

        [Menu("Random Delay (ms)", "Random delay added to action delay (0-100ms)")]
        public RangeNode<int> RandomDelay { get; set; } = new(25, 0, 100);

        [Menu("Cancel With Right Mouse Button", "Cancel operation on manual right-click")]
        public ToggleNode CancelWithRightClick { get; set; } = new(true);

        [Menu("Defer existing items", "Defer items that are already deferred")]
        public ToggleNode DeferExistingItems { get; set; } = new(true);

        [Menu("Auto Confirm", "Automatically click confirm button after deferring")]
        public ToggleNode AutoConfirm { get; set; } = new(false);

        [Menu("Auto Pickup", "Automatically ctrl+click deferred items after confirming (requires Auto Confirm)")]
        public ToggleNode AutoPickup { get; set; } = new(false);

        [Menu("Auto Reroll", "Automatically click reroll button after confirm/pickup (requires Auto Confirm)")]
        public ToggleNode AutoReroll { get; set; } = new(false);

        [Menu("Enable API Integration", "Auto-populate defer list from PoE2 Scout API")]
        public ToggleNode EnableApiIntegration { get; set; } = new(false);

        [Menu("Use NinjaPricer Data", "Read data from NinjaPricer cache instead of making API calls")]
        public ToggleNode UseNinjaPricerData { get; set; } = new(false);

        [Menu("League Name", "Current PoE2 league name for API requests")]
        public TextNode LeagueName { get; set; } = new("Rise of the Abyssal");

        [Menu("Minimum Exalted Value", "Minimum exalted orb value to include items in defer list")]
        public RangeNode<float> MinExaltedValue { get; set; } = new(1.0f, 0.01f, 50f);

        [Menu("Auto-Update Interval (minutes)", "How often to fetch new data from API")]
        public RangeNode<int> ApiUpdateInterval { get; set; } = new(30, 5, 180);

        [Menu("Replace Manual Items", "Replace manually configured items with API data")]
        public ToggleNode ReplaceManualItems { get; set; } = new(false);

        public string LastApiUpdateTime { get; set; } = string.Empty;

        public DeferGroup DeferGroup { get; set; } = new();
    }
}