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

        public ToggleNode IncludeCurrencyItems { get; set; } = new(true);

        public RangeNode<float> MinCurrencyValue { get; set; } = new(1.0f, 0.01f, 50f);

        public ToggleNode IncludeRitualItems { get; set; } = new(true);

        public RangeNode<float> MinRitualValue { get; set; } = new(1.0f, 0.01f, 50f);

        public ToggleNode IncludeUniqueAccessories { get; set; } = new(true);

        public RangeNode<float> MinUniqueAccessoriesValue { get; set; } = new(5.0f, 0.01f, 500f);

        public ToggleNode IncludeUniqueArmour { get; set; } = new(true);

        public RangeNode<float> MinUniqueArmourValue { get; set; } = new(5.0f, 0.01f, 500f);

        public ToggleNode IncludeUniqueCharms { get; set; } = new(true);

        public RangeNode<float> MinUniqueCharmsValue { get; set; } = new(5.0f, 0.01f, 500f);

        public ToggleNode IncludeUniqueFlasks { get; set; } = new(true);

        public RangeNode<float> MinUniqueFlasksValue { get; set; } = new(5.0f, 0.01f, 500f);

        public ToggleNode IncludeUniqueIdols { get; set; } = new(true);

        public RangeNode<float> MinUniqueIdolsValue { get; set; } = new(5.0f, 0.01f, 500f);

        public ToggleNode IncludeUniqueJewels { get; set; } = new(true);

        public RangeNode<float> MinUniqueJewelsValue { get; set; } = new(5.0f, 0.01f, 500f);

        public ToggleNode IncludeUniqueWeapons { get; set; } = new(true);

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

        public bool TryGetStackableCategoryThreshold(string categoryKey, out decimal minValue)
        {
            minValue = 0m;

            return categoryKey switch
            {
                "currency" when IncludeCurrencyItems.Value => TryGetRangeValue(MinCurrencyValue, out minValue),
                "ritual" when IncludeRitualItems.Value => TryGetRangeValue(MinRitualValue, out minValue),
                _ => false
            };
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

        private static bool TryGetRangeValue(RangeNode<float>? node, out decimal value)
        {
            value = 0m;
            if (node == null)
            {
                return false;
            }

            value = (decimal)node.Value;
            return true;
        }
    }
}
