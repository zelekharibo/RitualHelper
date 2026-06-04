using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ExileCore2.PoEMemory.Components;
using ExileCore2.PoEMemory.MemoryObjects;
using ExileCore2.PoEMemory.Models;

namespace RitualHelper
{
    public class NinjaPricerBridgeService : IDisposable
    {
        private readonly Func<Entity, double?> _getEntityValueInChaos;
        private readonly Func<BaseItemType, double?> _getBaseItemTypeValueInChaos;
        private readonly Func<string, BaseItemType?> _findBaseItemTypeByName;
        private readonly Action<string> _logError;
        private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(5);

        private DateTime _lastExaltedValueRefresh = DateTime.MinValue;
        private decimal? _cachedChaosPerExalted;

        public NinjaPricerBridgeService(
            Func<Entity, double?> getEntityValueInChaos,
            Func<BaseItemType, double?> getBaseItemTypeValueInChaos,
            Func<string, BaseItemType?> findBaseItemTypeByName,
            Action<string>? logInfo = null,
            Action<string>? logError = null)
        {
            _getEntityValueInChaos = getEntityValueInChaos ?? throw new ArgumentNullException(nameof(getEntityValueInChaos));
            _getBaseItemTypeValueInChaos = getBaseItemTypeValueInChaos ?? throw new ArgumentNullException(nameof(getBaseItemTypeValueInChaos));
            _findBaseItemTypeByName = findBaseItemTypeByName ?? throw new ArgumentNullException(nameof(findBaseItemTypeByName));
            _logError = logError ?? (msg => Console.WriteLine($"RitualHelper ERROR: {msg}"));
        }

        public Task<DeferItem?> GetFallbackDeferItemAsync(
            Entity itemEntity,
            BaseItemType baseItemType,
            string itemMatchName,
            int stackSize,
            decimal? minCurrencyValue,
            decimal? minRitualValue,
            IReadOnlyDictionary<string, decimal>? uniqueCategoryThresholds = null)
        {
            return Task.FromResult(
                TryGetFallbackDeferItemCached(
                    itemEntity,
                    baseItemType,
                    itemMatchName,
                    stackSize,
                    minCurrencyValue,
                    minRitualValue,
                    uniqueCategoryThresholds));
        }

        public DeferItem? TryGetFallbackDeferItemCached(
            Entity itemEntity,
            BaseItemType baseItemType,
            string itemMatchName,
            int stackSize,
            decimal? minCurrencyValue,
            decimal? minRitualValue,
            IReadOnlyDictionary<string, decimal>? uniqueCategoryThresholds = null)
        {
            if (itemEntity == null || baseItemType == null)
            {
                return null;
            }

            var exaltedValue = GetEntityValueInExalted(itemEntity);
            if (!exaltedValue.HasValue || exaltedValue <= 0)
            {
                return null;
            }

            if (TryGetUniqueMinimumValue(itemEntity, baseItemType, uniqueCategoryThresholds, out var uniqueMinValue) &&
                exaltedValue.Value >= uniqueMinValue)
            {
                return new DeferItem(
                    NormalizeItemNameForMatching(itemMatchName),
                    CalculatePriority(exaltedValue.Value),
                    1,
                    true);
            }

            if (IsCurrencyItem(baseItemType, itemEntity) && minCurrencyValue.HasValue)
            {
                var minStackSize = CalculateMinimumStackSize(exaltedValue.Value, minCurrencyValue.Value);
                if (stackSize >= minStackSize)
                {
                    return new DeferItem(
                        NormalizeItemNameForMatching(itemMatchName),
                        CalculatePriority(exaltedValue.Value),
                        minStackSize,
                        true);
                }

                return null;
            }

            if (IsRitualItem(baseItemType, itemEntity, itemMatchName) && minRitualValue.HasValue)
            {
                var minStackSize = CalculateMinimumStackSize(exaltedValue.Value, minRitualValue.Value);
                if (stackSize >= minStackSize)
                {
                    return new DeferItem(
                        NormalizeItemNameForMatching(itemMatchName),
                        CalculatePriority(exaltedValue.Value),
                        minStackSize,
                        true);
                }
            }

            return null;
        }

        private decimal? GetEntityValueInExalted(Entity itemEntity)
        {
            try
            {
                var chaosValue = _getEntityValueInChaos(itemEntity);
                if (!chaosValue.HasValue || chaosValue <= 0)
                {
                    return null;
                }

                var chaosPerExalted = GetChaosPerExalted();
                if (!chaosPerExalted.HasValue || chaosPerExalted <= 0)
                {
                    return null;
                }

                return (decimal)chaosValue.Value / chaosPerExalted.Value;
            }
            catch (Exception ex)
            {
                _logError($"error pricing ritual item through PluginBridge: {ex.Message}");
                return null;
            }
        }

        private decimal? GetChaosPerExalted()
        {
            if (DateTime.Now - _lastExaltedValueRefresh < _cacheExpiry && _cachedChaosPerExalted.HasValue)
            {
                return _cachedChaosPerExalted;
            }

            try
            {
                var exaltedOrb = _findBaseItemTypeByName("Exalted Orb");
                if (exaltedOrb == null)
                {
                    _logError("failed to resolve Exalted Orb base item type");
                    return _cachedChaosPerExalted;
                }

                var exaltedChaosValue = _getBaseItemTypeValueInChaos(exaltedOrb);
                if (!exaltedChaosValue.HasValue || exaltedChaosValue <= 0)
                {
                    _logError("PluginBridge returned an invalid Exalted Orb price");
                    return _cachedChaosPerExalted;
                }

                _cachedChaosPerExalted = (decimal)exaltedChaosValue.Value;
                _lastExaltedValueRefresh = DateTime.Now;
                return _cachedChaosPerExalted;
            }
            catch (Exception ex)
            {
                _logError($"error refreshing Exalted Orb price: {ex.Message}");
                return _cachedChaosPerExalted;
            }
        }

        private static string NormalizeItemNameForMatching(string itemName)
        {
            return itemName?.Replace('\x2019', '\x27') ?? string.Empty;
        }

        private static int CalculatePriority(decimal exaltedValue)
        {
            return exaltedValue switch
            {
                >= 500m => 10,
                >= 400m => 9,
                >= 300m => 8,
                >= 200m => 7,
                >= 100m => 6,
                >= 50m => 5,
                >= 25m => 4,
                >= 10m => 3,
                >= 5m => 2,
                _ => 1
            };
        }

        private static int CalculateMinimumStackSize(decimal itemValueInExalted, decimal minValueInExalted)
        {
            if (itemValueInExalted <= 0)
            {
                return int.MaxValue;
            }

            var requiredStacks = decimal.Ceiling(minValueInExalted / itemValueInExalted);
            if (requiredStacks <= 1)
            {
                return 1;
            }

            return requiredStacks >= int.MaxValue ? int.MaxValue : (int)requiredStacks;
        }

        private static bool TryGetUniqueMinimumValue(
            Entity itemEntity,
            BaseItemType baseItemType,
            IReadOnlyDictionary<string, decimal>? uniqueCategoryThresholds,
            out decimal minValue)
        {
            minValue = 0m;

            if (uniqueCategoryThresholds == null || uniqueCategoryThresholds.Count == 0)
            {
                return false;
            }

            var category = GetUniqueCategory(itemEntity, baseItemType);
            return !string.IsNullOrWhiteSpace(category) &&
                   uniqueCategoryThresholds.TryGetValue(category, out minValue);
        }

        private static string GetUniqueCategory(Entity itemEntity, BaseItemType baseItemType)
        {
            if (!itemEntity.TryGetComponent<Mods>(out var mods) || mods.ItemRarity != ExileCore2.Shared.Enums.ItemRarity.Unique)
            {
                return string.Empty;
            }

            var parts = new[]
            {
                baseItemType.ClassName,
                baseItemType.BaseName,
                itemEntity.Metadata
            };
            var probe = string.Join("|", parts.Where(x => !string.IsNullOrWhiteSpace(x))).ToLowerInvariant();

            if (ContainsAny(probe, "ring", "amulet", "belt", "accessory"))
            {
                return "accessory";
            }

            if (ContainsAny(probe, "charm"))
            {
                return "charm";
            }

            if (ContainsAny(probe, "flask"))
            {
                return "flask";
            }

            if (ContainsAny(probe, "idol"))
            {
                return "idol";
            }

            if (ContainsAny(probe, "jewel"))
            {
                return "jewel";
            }

            if (ContainsAny(probe, "helmet", "body armour", "bodyarmour", "boots", "gloves", "shield", "focus", "armour"))
            {
                return "armour";
            }

            if (ContainsAny(probe, "axe", "bow", "claw", "crossbow", "dagger", "flail", "mace", "quarterstaff", "spear", "staff", "sword", "wand", "weapon"))
            {
                return "weapon";
            }

            return string.Empty;
        }

        private static bool IsCurrencyItem(BaseItemType baseItemType, Entity itemEntity)
        {
            var probe = $"{baseItemType.ClassName}|{baseItemType.BaseName}|{itemEntity.Metadata}".ToLowerInvariant();
            return probe.Contains("currency", StringComparison.Ordinal) && !probe.Contains("omen", StringComparison.Ordinal);
        }

        private static bool IsRitualItem(BaseItemType baseItemType, Entity itemEntity, string itemMatchName)
        {
            var probe = $"{baseItemType.ClassName}|{baseItemType.BaseName}|{itemEntity.Metadata}|{itemMatchName}".ToLowerInvariant();
            return probe.Contains("omen", StringComparison.Ordinal) ||
                   probe.Contains("ritual", StringComparison.Ordinal);
        }

        private static bool ContainsAny(string value, params string[] needles)
        {
            return needles.Any(value.Contains);
        }

        public void Dispose()
        {
        }
    }
}
