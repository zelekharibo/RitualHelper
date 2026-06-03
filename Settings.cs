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

        [Menu("Minimum Unique Value", "Minimum exalted orb value to include unique items in defer list")]
        public RangeNode<float> MinUniqueExaltedValue { get; set; } = new(5.0f, 0.01f, 500f);

        [Menu("Unique Accessories", "Include unique accessories from API/cache")]
        public ToggleNode IncludeUniqueAccessories { get; set; } = new(true);

        [Menu("Unique Accessories Min", "Minimum exalted value for unique accessories")]
        public RangeNode<float> MinUniqueAccessoriesValue { get; set; } = new(5.0f, 0.01f, 500f);

        [Menu("Unique Armour", "Include unique armour from API/cache")]
        public ToggleNode IncludeUniqueArmour { get; set; } = new(true);

        [Menu("Unique Armour Min", "Minimum exalted value for unique armour")]
        public RangeNode<float> MinUniqueArmourValue { get; set; } = new(5.0f, 0.01f, 500f);

        [Menu("Unique Charms", "Include unique charms from API/cache")]
        public ToggleNode IncludeUniqueCharms { get; set; } = new(true);

        [Menu("Unique Charms Min", "Minimum exalted value for unique charms")]
        public RangeNode<float> MinUniqueCharmsValue { get; set; } = new(5.0f, 0.01f, 500f);

        [Menu("Unique Flasks", "Include unique flasks from API/cache")]
        public ToggleNode IncludeUniqueFlasks { get; set; } = new(true);

        [Menu("Unique Flasks Min", "Minimum exalted value for unique flasks")]
        public RangeNode<float> MinUniqueFlasksValue { get; set; } = new(5.0f, 0.01f, 500f);

        [Menu("Unique Idols", "Include unique idols from API/cache")]
        public ToggleNode IncludeUniqueIdols { get; set; } = new(true);

        [Menu("Unique Idols Min", "Minimum exalted value for unique idols")]
        public RangeNode<float> MinUniqueIdolsValue { get; set; } = new(5.0f, 0.01f, 500f);

        [Menu("Unique Jewels", "Include unique jewels from API/cache")]
        public ToggleNode IncludeUniqueJewels { get; set; } = new(true);

        [Menu("Unique Jewels Min", "Minimum exalted value for unique jewels")]
        public RangeNode<float> MinUniqueJewelsValue { get; set; } = new(5.0f, 0.01f, 500f);

        [Menu("Unique Weapons", "Include unique weapons from API/cache")]
        public ToggleNode IncludeUniqueWeapons { get; set; } = new(true);

        [Menu("Unique Weapons Min", "Minimum exalted value for unique weapons")]
        public RangeNode<float> MinUniqueWeaponsValue { get; set; } = new(5.0f, 0.01f, 500f);

        [Menu("Include Valuable Unlisted Items", "Consider ritual items above the configured value thresholds even if they are not in the accepted items list")]
        public ToggleNode IncludeValuableUnlistedItems { get; set; } = new(false);

        [Menu("Draw Debug Overlay", "Draw ritual item debug rectangles and labels")]
        public ToggleNode DrawDebugOverlay { get; set; } = new(false);

        [Menu("Auto-Update Interval (minutes)", "How often to fetch new data from API")]
        public RangeNode<int> ApiUpdateInterval { get; set; } = new(30, 5, 180);

        [Menu("Replace Manual Items", "Replace manually configured items with API data")]
        public ToggleNode ReplaceManualItems { get; set; } = new(false);

        public string LastApiUpdateTime { get; set; } = string.Empty;

        public DeferGroup DeferGroup { get; set; } = new();

        public Dictionary<string, decimal> GetEnabledUniqueCategoryThresholds()
        {
            var thresholds = new Dictionary<string, decimal>();

            AddUniqueCategoryThreshold(thresholds, IncludeUniqueAccessories, MinUniqueAccessoriesValue, "accessory");
            AddUniqueCategoryThreshold(thresholds, IncludeUniqueArmour, MinUniqueArmourValue, "armour");
            AddUniqueCategoryThreshold(thresholds, IncludeUniqueCharms, MinUniqueCharmsValue, "charm");
            AddUniqueCategoryThreshold(thresholds, IncludeUniqueFlasks, MinUniqueFlasksValue, "flask");
            AddUniqueCategoryThreshold(thresholds, IncludeUniqueIdols, MinUniqueIdolsValue, "idol");
            AddUniqueCategoryThreshold(thresholds, IncludeUniqueJewels, MinUniqueJewelsValue, "jewel");
            AddUniqueCategoryThreshold(thresholds, IncludeUniqueWeapons, MinUniqueWeaponsValue, "weapon");

            return thresholds;
        }

        private static void AddUniqueCategoryThreshold(
            IDictionary<string, decimal> thresholds,
            ToggleNode? enabledNode,
            RangeNode<float>? minValueNode,
            string categoryApiId)
        {
            if (enabledNode?.Value != true || minValueNode == null)
            {
                return;
            }

            thresholds[categoryApiId] = (decimal)minValueNode.Value;
        }
    }
}
