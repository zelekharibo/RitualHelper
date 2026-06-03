using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using ExileCore2.Shared;

namespace RitualHelper
{
    public class PoE2ScoutApiService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl = "https://poe2scout.com/api";
        private readonly string _realm = "poe2";
        private readonly string _leagueName;
        private readonly Action<string> _logInfo;
        private readonly Action<string> _logError;
        private readonly bool _useNinjaPricerData;
        
        private DateTime _lastFetch = DateTime.MinValue;
        private List<PoE2ScoutItem> _cachedCurrency = new();
        private List<PoE2ScoutItem> _cachedOmens = new();
        private List<PoE2ScoutItem> _cachedUniques = new();
        private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(5);
        private DateTime _lastRequestTime = DateTime.MinValue;
        private readonly TimeSpan _requestDelay = TimeSpan.FromSeconds(5);
        private List<string>? _cachedUniqueCategoryApiIds;

        public PoE2ScoutApiService(string leagueName = "Rise of the Abyssal", 
            Action<string>? logInfo = null, Action<string>? logError = null, bool useNinjaPricerData = false)
        {
            _logInfo = logInfo ?? (msg => Console.WriteLine($"RitualHelper: {msg}"));
            _logError = logError ?? (msg => Console.WriteLine($"RitualHelper ERROR: {msg}"));
            _leagueName = leagueName;
            _useNinjaPricerData = useNinjaPricerData;
            
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            
            SetupUserAgent();
            _logInfo($"API Service initialized for League: {leagueName}, UseNinjaPricerData: {useNinjaPricerData}");
        }
        
        private void SetupUserAgent()
        {
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "RitualHelper/1.0");
            _logInfo("using default User-Agent");
        }

        public async Task<List<PoE2ScoutItem>> GetCurrencyDataAsync()
        {
            if (IsCacheValid(_cachedCurrency))
            {
                return _cachedCurrency;
            }

            try
            {
                if (_useNinjaPricerData)
                {
                    _logInfo("Reading currency data from NinjaPricer cache...");
                    _cachedCurrency = await LoadDataFromNinjaPricerCache("currency") ?? new List<PoE2ScoutItem>();
                }
                else
                {
                    _logInfo("Fetching currency data from API...");
                    _cachedCurrency = await LoadPagedDataFromApi("currency") ?? new List<PoE2ScoutItem>();
                }
                
                if (_cachedCurrency.Any())
                {
                    _lastFetch = DateTime.Now;
                    _logInfo($"Successfully loaded {_cachedCurrency.Count} currency items");
                }
                else
                {
                    _logInfo("No currency items found");
                }
                
                return _cachedCurrency;
            }
            catch (Exception ex)
            {
                _logError($"Error loading currency data: {ex.Message}");
                LogInnerException(ex);
                return _cachedCurrency; // return cached data on error
            }
        }

        public async Task<List<PoE2ScoutItem>> GetRitualOmensAsync()
        {
            if (IsCacheValid(_cachedOmens))
            {
                return _cachedOmens;
            }

            try
            {
                if (_useNinjaPricerData)
                {
                    _logInfo("Reading ritual omen data from NinjaPricer cache...");
                    _cachedOmens = await LoadDataFromNinjaPricerCache("ritual") ?? new List<PoE2ScoutItem>();
                }
                else
                {
                    _logInfo("Fetching ritual omen data from API...");
                    _cachedOmens = await LoadPagedDataFromApi("ritual") ?? new List<PoE2ScoutItem>();
                }
                
                if (_cachedOmens.Any())
                {
                    _lastFetch = DateTime.Now;
                    _logInfo($"Successfully loaded {_cachedOmens.Count} ritual omens");
                }
                else
                {
                    _logInfo("No ritual omens found");
                }
                
                return _cachedOmens;
            }
            catch (Exception ex)
            {
                _logError($"Error loading ritual omen data: {ex.Message}");
                LogInnerException(ex);
                return _cachedOmens; // return cached data on error
            }
        }

        public async Task<List<PoE2ScoutItem>> GetUniqueItemsAsync()
        {
            if (IsCacheValid(_cachedUniques))
            {
                return _cachedUniques;
            }

            try
            {
                if (_useNinjaPricerData)
                {
                    _logInfo("Reading unique item data from NinjaPricer cache...");
                    _cachedUniques = await LoadUniqueDataFromNinjaPricerCache();
                }
                else
                {
                    _logInfo("Fetching unique item data from API...");
                    _cachedUniques = await LoadUniqueDataFromApi();
                }

                if (_cachedUniques.Any())
                {
                    _lastFetch = DateTime.Now;
                    _logInfo($"Successfully loaded {_cachedUniques.Count} unique items");
                }
                else
                {
                    _logInfo("No unique items found");
                }

                return _cachedUniques;
            }
            catch (Exception ex)
            {
                _logError($"Error loading unique item data: {ex.Message}");
                LogInnerException(ex);
                return _cachedUniques;
            }
        }

        public async Task<List<DeferItem>> GenerateDeferListAsync(
            decimal? minCurrencyValue = null,
            decimal? minRitualValue = null,
            IReadOnlyDictionary<string, decimal>? uniqueCategoryThresholds = null)
        {
            var deferItems = new List<DeferItem>();

            try
            {
                // fetch and filter valuable items
                var currencyData = await GetCurrencyDataAsync();
                var omenData = await GetRitualOmensAsync();
                var uniqueData = await GetUniqueItemsAsync();
                
                var valuableCurrency = FilterStackableItems(currencyData, minCurrencyValue);
                var valuableOmens = FilterStackableItems(omenData, minRitualValue);
                var valuableUniques = FilterUniqueItems(uniqueData, uniqueCategoryThresholds);

                // convert to defer items with API prefix
                AddItemsToDefer(deferItems, valuableCurrency, minCurrencyValue, true);
                AddItemsToDefer(deferItems, valuableOmens, minRitualValue, true);
                AddItemsToDefer(deferItems, valuableUniques);

                _logInfo($"Generated {deferItems.Count} defer items from API data");
                return deferItems
                    .OrderByDescending(d => d.Priority)
                    .ThenBy(d => d.Name)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logError($"Error generating defer list: {ex.Message}");
                return deferItems;
            }
        }

        public async Task<DeferItem?> GetFallbackDeferItemAsync(
            string itemBaseName,
            int stackSize,
            decimal? minCurrencyValue,
            decimal? minRitualValue,
            IReadOnlyDictionary<string, decimal>? uniqueCategoryThresholds = null)
        {
            await EnsureAllDataLoadedAsync();
            return TryGetFallbackDeferItemCached(
                itemBaseName,
                stackSize,
                minCurrencyValue,
                minRitualValue,
                uniqueCategoryThresholds);
        }

        public DeferItem? TryGetFallbackDeferItemCached(
            string itemBaseName,
            int stackSize,
            decimal? minCurrencyValue,
            decimal? minRitualValue,
            IReadOnlyDictionary<string, decimal>? uniqueCategoryThresholds = null)
        {
            var currencyMatch = FindFallbackMatch(_cachedCurrency, itemBaseName);
            if (currencyMatch != null && minCurrencyValue.HasValue)
            {
                var minStackSize = CalculateMinimumStackSize(currencyMatch, minCurrencyValue.Value);
                if (currencyMatch.ForceInclude || stackSize >= minStackSize)
                {
                    return new DeferItem(
                        NormalizeApiItemNameForMatching(currencyMatch.GetName()),
                        CalculatePriority(currencyMatch.GetExaltedValue()),
                        minStackSize,
                        true);
                }
            }

            var ritualMatch = FindFallbackMatch(_cachedOmens, itemBaseName);
            if (ritualMatch != null && minRitualValue.HasValue)
            {
                var minStackSize = CalculateMinimumStackSize(ritualMatch, minRitualValue.Value);
                if (ritualMatch.ForceInclude || stackSize >= minStackSize)
                {
                    return new DeferItem(
                        NormalizeApiItemNameForMatching(ritualMatch.GetName()),
                        CalculatePriority(ritualMatch.GetExaltedValue()),
                        minStackSize,
                        true);
                }
            }

            var uniqueMatch = FindFallbackMatch(_cachedUniques, itemBaseName);
            if (uniqueMatch != null &&
                TryGetUniqueMinimumValue(uniqueMatch, uniqueCategoryThresholds, out var uniqueMinValue) &&
                (uniqueMatch.ForceInclude || uniqueMatch.GetExaltedValue() >= uniqueMinValue))
            {
                return new DeferItem(
                    NormalizeApiItemNameForMatching(uniqueMatch.GetName()),
                    CalculatePriority(uniqueMatch.GetExaltedValue()),
                    1,
                    true);
            }

            return null;
        }

        private async Task EnsureAllDataLoadedAsync()
        {
            await GetCurrencyDataAsync();
            await GetRitualOmensAsync();
            await GetUniqueItemsAsync();
        }

        private async Task<List<PoE2ScoutItem>> LoadUniqueDataFromNinjaPricerCache()
        {
            var uniqueFileNames = new[]
            {
                "Accessories",
                "Armour",
                "Charms",
                "Flasks",
                "Idols",
                "Jewels",
                "Weapons"
            };

            var allItems = new List<PoE2ScoutItem>();

            foreach (var fileName in uniqueFileNames)
            {
                var items = await LoadDataFromNinjaPricerCache(fileName);
                if (items.Any())
                {
                    allItems.AddRange(items);
                }
            }

            return allItems
                .GroupBy(item => item.GetName(), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderBy(item => item.GetExaltedValue()).First())
                .ToList();
        }

        private async Task<List<PoE2ScoutItem>> LoadDataFromNinjaPricerCache(string category)
        {
            try
            {
                // construct path to NinjaPricer cache file
                var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                var ninjaPricerPath = Path.Combine(baseDirectory, "Plugins", "Temp", "NinjaPricer", "poescoutdata", _leagueName, $"{category}.json");
                
                _logInfo($"Looking for NinjaPricer cache file: {ninjaPricerPath}");
                
                if (!File.Exists(ninjaPricerPath))
                {
                    _logError($"NinjaPricer cache file not found: {ninjaPricerPath}");
                    return new List<PoE2ScoutItem>();
                }
                
                // read and parse the JSON file
                var jsonContent = await File.ReadAllTextAsync(ninjaPricerPath);
                if (string.IsNullOrEmpty(jsonContent))
                {
                    _logError($"NinjaPricer cache file is empty: {ninjaPricerPath}");
                    return new List<PoE2ScoutItem>();
                }
                
                _logInfo($"Read {jsonContent.Length} characters from NinjaPricer cache file");
                
                var options = new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                
                var items = DeserializeNinjaPricerItems(jsonContent, options);
                
                _logInfo($"Successfully parsed {items.Count} items from NinjaPricer cache");
                return items;
            }
            catch (Exception ex)
            {
                _logError($"Error reading NinjaPricer cache for {category}: {ex.Message}");
                LogInnerException(ex);
                return new List<PoE2ScoutItem>();
            }
        }

        private async Task<List<PoE2ScoutItem>> LoadPagedDataFromApi(string category)
        {
            var items = new List<PoE2ScoutItem>();
            var page = 1;
            PoE2ScoutApiResponse container;
            
            _logInfo($"Starting {category} data download...");
            
            do
            {
                var url = BuildApiUrl(category, page);
                
                try
                {
                    // always apply rate limit first, even if previous calls failed
                    await ApplyRateLimit();
                    
                    var jsonResponse = await _httpClient.GetStringAsync(url);
                    if (string.IsNullOrEmpty(jsonResponse))
                    {
                        _logError($"Page {page} returned empty response");
                        break;
                    }
                    
                    container = DeserializeApiResponse(jsonResponse, page);
                    if (container == null) break;
                    
                    if (container.Items?.Any() == true)
                    {
                        items.AddRange(container.Items);
                        LogDownloadProgress(page, container);
                    }
                    else
                    {
                        if (page == 1) _logInfo($"Page {page} returned no items");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logError($"Failed to download page {page} for {category}: {ex.Message}");
                    LogInnerException(ex);
                    // note: rate limit was already applied, so timing is maintained even on error
                    break;
                }
                
                page++;
            } while (container.CurrentPage < container.Pages);
            
            _logInfo($"Finished downloading {category}: {items.Count} total items");
            return items;
        }

        private async Task<List<PoE2ScoutItem>> LoadUniqueDataFromApi()
        {
            var uniqueCategoryApiIds = await GetUniqueCategoryApiIdsAsync();
            var allItems = new List<PoE2ScoutItem>();

            foreach (var categoryApiId in uniqueCategoryApiIds)
            {
                var categoryItems = await LoadPagedUniqueDataFromApi(categoryApiId);
                if (categoryItems.Any())
                {
                    allItems.AddRange(categoryItems);
                }
            }

            return allItems
                .GroupBy(item => item.GetName(), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderBy(item => item.GetExaltedValue()).First())
                .ToList();
        }

        private async Task<List<string>> GetUniqueCategoryApiIdsAsync()
        {
            if (_cachedUniqueCategoryApiIds?.Any() == true)
            {
                return _cachedUniqueCategoryApiIds;
            }

            try
            {
                await ApplyRateLimit();

                var url = BuildItemCategoriesUrl();
                var jsonResponse = await _httpClient.GetStringAsync(url);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var categoriesResponse = JsonSerializer.Deserialize<CategoriesResponse>(jsonResponse, options);
                var categoryApiIds = categoriesResponse?.UniqueCategories?
                    .Select(category => category.ApiId)
                    .Where(apiId => !string.IsNullOrWhiteSpace(apiId))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (categoryApiIds?.Any() == true)
                {
                    _cachedUniqueCategoryApiIds = categoryApiIds;
                    return categoryApiIds;
                }
            }
            catch (Exception ex)
            {
                _logError($"Error loading unique categories: {ex.Message}");
                LogInnerException(ex);
            }

            _cachedUniqueCategoryApiIds = new List<string>
            {
                "accessory",
                "armour",
                "flask",
                "jewel",
                "weapon"
            };

            return _cachedUniqueCategoryApiIds;
        }

        private async Task<List<PoE2ScoutItem>> LoadPagedUniqueDataFromApi(string category)
        {
            var items = new List<PoE2ScoutItem>();
            var page = 1;
            PoE2ScoutApiResponse container;

            _logInfo($"Starting unique {category} data download...");

            do
            {
                var url = BuildUniqueApiUrl(category, page);

                try
                {
                    await ApplyRateLimit();

                    var jsonResponse = await _httpClient.GetStringAsync(url);
                    if (string.IsNullOrEmpty(jsonResponse))
                    {
                        _logError($"Unique page {page} returned empty response for {category}");
                        break;
                    }

                    container = DeserializeApiResponse(jsonResponse, page);
                    if (container == null) break;

                    if (container.Items?.Any() == true)
                    {
                        items.AddRange(container.Items);
                        LogDownloadProgress(page, container);
                    }
                    else
                    {
                        if (page == 1) _logInfo($"Unique page {page} returned no items for {category}");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logError($"Failed to download unique page {page} for {category}: {ex.Message}");
                    LogInnerException(ex);
                    break;
                }

                page++;
            } while (container.CurrentPage < container.Pages);

            _logInfo($"Finished downloading unique {category}: {items.Count} total items");
            return items;
        }

        private static List<PoE2ScoutItem> DeserializeNinjaPricerItems(string jsonContent, JsonSerializerOptions options)
        {
            using var document = JsonDocument.Parse(jsonContent);

            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                return JsonSerializer.Deserialize<List<PoE2ScoutItem>>(jsonContent, options) ?? new List<PoE2ScoutItem>();
            }

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new List<PoE2ScoutItem>();
            }

            var root = document.RootElement;

            if (LooksLikeNinjaPricerCache(root))
            {
                return DeserializeNinjaPricerCache(root, options);
            }

            if (TryDeserializeItemsProperty(root, "items", options, out var items) ||
                TryDeserializeItemsProperty(root, "data", options, out items) ||
                TryDeserializeItemsProperty(root, "result", options, out items) ||
                TryDeserializeItemsProperty(root, "value", options, out items))
            {
                return items;
            }

            if (LooksLikeScoutItem(root))
            {
                var item = JsonSerializer.Deserialize<PoE2ScoutItem>(jsonContent, options);
                return item == null ? new List<PoE2ScoutItem>() : new List<PoE2ScoutItem> { item };
            }

            return new List<PoE2ScoutItem>();
        }

        private static List<PoE2ScoutItem> DeserializeNinjaPricerCache(JsonElement root, JsonSerializerOptions options)
        {
            var metadataById = new Dictionary<string, NinjaPricerItemMetadata>(StringComparer.OrdinalIgnoreCase);

            if (TryGetPropertyIgnoreCase(root, "items", out var itemsElement) &&
                itemsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in itemsElement.EnumerateArray())
                {
                    var metadata = JsonSerializer.Deserialize<NinjaPricerItemMetadata>(element.GetRawText(), options);
                    if (metadata != null && !string.IsNullOrWhiteSpace(metadata.Id))
                    {
                        metadataById[metadata.Id] = metadata;
                    }
                }
            }

            var core = TryGetPropertyIgnoreCase(root, "core", out var coreElement) && coreElement.ValueKind == JsonValueKind.Object
                ? JsonSerializer.Deserialize<NinjaPricerCore>(coreElement.GetRawText(), options)
                : null;

            var results = new List<PoE2ScoutItem>();

            if (TryGetPropertyIgnoreCase(root, "lines", out var linesElement) &&
                linesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in linesElement.EnumerateArray())
                {
                    var line = JsonSerializer.Deserialize<NinjaPricerLine>(element.GetRawText(), options);
                    var lineApiId = line?.GetApiId();
                    if (line == null || string.IsNullOrWhiteSpace(lineApiId))
                    {
                        continue;
                    }

                    metadataById.TryGetValue(lineApiId, out var metadata);
                    var item = MapNinjaPricerItem(line, metadata, core);
                    if (item != null)
                    {
                        results.Add(item);
                    }
                }
            }

            if (results.Count > 0)
            {
                return results;
            }

            if (TryGetPropertyIgnoreCase(root, "LinesByName", out var linesByNameElement) &&
                linesByNameElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in linesByNameElement.EnumerateObject())
                {
                    if (entry.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var pair = JsonSerializer.Deserialize<NinjaPricerLinePair>(entry.Value.GetRawText(), options);
                    var pairApiId = pair?.Item1?.GetApiId();
                    if (pair?.Item1 == null || string.IsNullOrWhiteSpace(pairApiId))
                    {
                        continue;
                    }

                    var metadata = pair.Item2;
                    if (metadata == null && metadataById.TryGetValue(pairApiId, out var existingMetadata))
                    {
                        metadata = existingMetadata;
                    }

                    var item = MapNinjaPricerItem(pair.Item1, metadata, core);
                    if (item != null)
                    {
                        results.Add(item);
                    }
                }
            }

            return results;
        }

        private static PoE2ScoutItem? MapNinjaPricerItem(NinjaPricerLine line, NinjaPricerItemMetadata? metadata, NinjaPricerCore? core)
        {
            var apiId = !string.IsNullOrWhiteSpace(metadata?.Id)
                ? metadata.Id
                : line.GetApiId();
            if (string.IsNullOrWhiteSpace(apiId))
            {
                return null;
            }

            var itemName = metadata?.Name;
            if (string.IsNullOrWhiteSpace(itemName))
            {
                itemName = line.GetDisplayName();
            }

            var iconUrl = NormalizeNinjaPricerImageUrl(metadata?.Image);
            if (string.IsNullOrWhiteSpace(iconUrl))
            {
                iconUrl = NormalizeNinjaPricerImageUrl(line.Icon);
            }

            var categoryApiId = metadata?.Category;
            if (string.IsNullOrWhiteSpace(categoryApiId))
            {
                categoryApiId = NormalizeCategoryApiId(line.Category);
            }

            return new PoE2ScoutItem
            {
                ApiId = apiId,
                Text = itemName ?? apiId,
                CategoryApiId = categoryApiId ?? string.Empty,
                IconUrl = iconUrl,
                CurrentPrice = ConvertNinjaPricerValueToExalted(line.PrimaryValue, core),
                ForceInclude = ShouldForceIncludeNinjaPricerItem(line.PrimaryValue, core)
            };
        }

        private static decimal ConvertNinjaPricerValueToExalted(decimal primaryValue, NinjaPricerCore? core)
        {
            if (core == null || string.IsNullOrWhiteSpace(core.Primary))
            {
                return primaryValue;
            }

            if (string.Equals(core.Primary, "exalted", StringComparison.OrdinalIgnoreCase))
            {
                return primaryValue;
            }

            if (core.Rates.TryGetValue("exalted", out var exaltedRate) && exaltedRate.HasValue)
            {
                return primaryValue * exaltedRate.Value;
            }

            return primaryValue;
        }

        private static bool ShouldForceIncludeNinjaPricerItem(decimal primaryValue, NinjaPricerCore? core)
        {
            if (primaryValue <= 0 || core == null)
            {
                return false;
            }

            var hasExaltedRate = core.Rates.TryGetValue("exalted", out var exaltedRate) && exaltedRate.HasValue;
            var hasDivinePricing = string.Equals(core.Primary, "divine", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(core.Secondary, "divine", StringComparison.OrdinalIgnoreCase);

            return !hasExaltedRate && hasDivinePricing;
        }

        private static string NormalizeNinjaPricerImageUrl(string? image)
        {
            if (string.IsNullOrWhiteSpace(image))
            {
                return string.Empty;
            }

            if (Uri.TryCreate(image, UriKind.Absolute, out _))
            {
                return image;
            }

            return $"https://web.poecdn.com{image}";
        }

        private static string NormalizeCategoryApiId(string? category)
        {
            if (string.IsNullOrWhiteSpace(category))
            {
                return string.Empty;
            }

            return category.Replace(" ", string.Empty).ToLowerInvariant() switch
            {
                "belt" => "accessory",
                "ring" => "accessory",
                "amulet" => "accessory",
                "accessories" => "accessory",
                "accessory" => "accessory",
                "focus" => "weapon",
                "crossbow" => "weapon",
                "bow" => "weapon",
                "wand" => "weapon",
                "staff" => "weapon",
                "spear" => "weapon",
                "quarterstaff" => "weapon",
                "mace" => "weapon",
                "flail" => "weapon",
                "axe" => "weapon",
                "sword" => "weapon",
                "helmet" => "armour",
                "bodyarmour" => "armour",
                "gloves" => "armour",
                "boots" => "armour",
                "shield" => "armour",
                "armours" => "armour",
                "armour" => "armour",
                "charms" => "charm",
                "charm" => "charm",
                "flasks" => "flask",
                "flask" => "flask",
                "idols" => "idol",
                "idol" => "idol",
                "jewels" => "jewel",
                "jewel" => "jewel",
                "maps" => "map",
                "map" => "map",
                "weapons" => "weapon",
                "weapon" => "weapon",
                "sanctumrelics" => "sanctum",
                "sanctumrelic" => "sanctum",
                _ => category.Replace(" ", string.Empty).ToLowerInvariant()
            };
        }

        private static bool LooksLikeNinjaPricerCache(JsonElement root)
        {
            return TryGetPropertyIgnoreCase(root, "lines", out _) ||
                   TryGetPropertyIgnoreCase(root, "LinesByName", out _);
        }

        private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        private static bool TryDeserializeItemsProperty(JsonElement root, string propertyName, JsonSerializerOptions options, out List<PoE2ScoutItem> items)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    items = JsonSerializer.Deserialize<List<PoE2ScoutItem>>(property.Value.GetRawText(), options) ?? new List<PoE2ScoutItem>();
                    return true;
                }

                if (property.Value.ValueKind == JsonValueKind.Object && LooksLikeScoutItem(property.Value))
                {
                    var item = JsonSerializer.Deserialize<PoE2ScoutItem>(property.Value.GetRawText(), options);
                    items = item == null ? new List<PoE2ScoutItem>() : new List<PoE2ScoutItem> { item };
                    return true;
                }
            }

            items = new List<PoE2ScoutItem>();
            return false;
        }

        private static bool LooksLikeScoutItem(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, "apiId", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(property.Name, "text", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(property.Name, "categoryApiId", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class NinjaPricerCore
        {
            public Dictionary<string, decimal?> Rates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public string Primary { get; set; } = string.Empty;
            public string Secondary { get; set; } = string.Empty;
        }

        private sealed class NinjaPricerLine
        {
            public JsonElement Id { get; set; }
            public string DetailsId { get; set; } = string.Empty;
            public string ItemId { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Icon { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
            public decimal PrimaryValue { get; set; }

            public string GetApiId()
            {
                if (!string.IsNullOrWhiteSpace(DetailsId))
                {
                    return DetailsId;
                }

                return Id.ValueKind switch
                {
                    JsonValueKind.String => Id.GetString() ?? string.Empty,
                    JsonValueKind.Number => Id.TryGetInt64(out var numericId) ? numericId.ToString() : string.Empty,
                    _ => string.Empty
                };
            }

            public string GetDisplayName()
            {
                if (!string.IsNullOrWhiteSpace(Name))
                {
                    return Name;
                }

                if (!string.IsNullOrWhiteSpace(ItemId))
                {
                    return ItemId;
                }

                return GetApiId();
            }
        }

        private sealed class NinjaPricerItemMetadata
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Image { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
        }

        private sealed class NinjaPricerLinePair
        {
            public NinjaPricerLine? Item1 { get; set; }
            public NinjaPricerItemMetadata? Item2 { get; set; }
        }

        private static int CalculatePriority(decimal exaltedValue)
        {
            return exaltedValue switch
            {
                >= 500m => 10,  // extremely valuable
                >= 400m => 9,   // very high value
                >= 300m => 8,   // high value
                >= 200m => 7,   // good value
                >= 100m => 6,   // decent value
                >= 50m => 5,    // moderate value
                >= 25m => 4,    // low-moderate value
                >= 10m => 3,    // low value
                >= 5m => 2,     // very low value
                _ => 1          // minimal value
            };
        }
        
        private bool IsCacheValid(List<PoE2ScoutItem> cachedData)
        {
            return DateTime.Now - _lastFetch < _cacheExpiry && cachedData.Any();
        }
        
        private static List<PoE2ScoutItem> FilterValuableItems(List<PoE2ScoutItem> items, decimal minValue)
        {
            return items
                .Where(item => item != null && (item.ForceInclude || item.GetExaltedValue() >= minValue))
                .OrderByDescending(item => item.GetExaltedValue())
                .ToList();
        }

        private static List<PoE2ScoutItem> FilterUniqueItems(
            List<PoE2ScoutItem> items,
            IReadOnlyDictionary<string, decimal>? uniqueCategoryThresholds)
        {
            return items
                .Where(item =>
                    item != null &&
                    TryGetUniqueMinimumValue(item, uniqueCategoryThresholds, out var minValue) &&
                    (item.ForceInclude || item.GetExaltedValue() >= minValue))
                .OrderByDescending(item => item.GetExaltedValue())
                .ToList();
        }

        private static List<PoE2ScoutItem> FilterStackableItems(List<PoE2ScoutItem> items, decimal? minValue)
        {
            if (!minValue.HasValue)
            {
                return new List<PoE2ScoutItem>();
            }

            return items
                .Where(item => item != null && (item.ForceInclude || item.GetExaltedValue() > 0))
                .OrderByDescending(item => item.GetExaltedValue())
                .ToList();
        }
        
        private static void AddItemsToDefer(List<DeferItem> deferItems, List<PoE2ScoutItem> apiItems)
        {
            foreach (var item in apiItems.Where(i => i != null && !string.IsNullOrEmpty(i.GetName())))
            {
                var priority = CalculatePriority(item.GetExaltedValue());
                deferItems.Add(new DeferItem(NormalizeApiItemNameForMatching(item.GetName()), priority, 1, true));
            }
        }

        private static void AddItemsToDefer(List<DeferItem> deferItems, List<PoE2ScoutItem> apiItems, decimal? minValue, bool useStackValue)
        {
            if (!minValue.HasValue)
            {
                return;
            }

            foreach (var item in apiItems.Where(i => i != null && !string.IsNullOrEmpty(i.GetName())))
            {
                var priority = CalculatePriority(item.GetExaltedValue());
                var minStackSize = useStackValue ? CalculateMinimumStackSize(item, minValue.Value) : 1;
                deferItems.Add(new DeferItem(NormalizeApiItemNameForMatching(item.GetName()), priority, minStackSize, true));
            }
        }

        private static string NormalizeApiItemNameForMatching(string itemName)
        {
            return itemName;
        }

        private static bool TryGetUniqueMinimumValue(
            PoE2ScoutItem item,
            IReadOnlyDictionary<string, decimal>? uniqueCategoryThresholds,
            out decimal minValue)
        {
            minValue = 0m;
            if (item == null)
            {
                return false;
            }

            if (uniqueCategoryThresholds == null || uniqueCategoryThresholds.Count == 0)
            {
                return false;
            }

            var normalizedCategory = NormalizeUniqueCategoryApiId(item.CategoryApiId);
            return uniqueCategoryThresholds.TryGetValue(normalizedCategory, out minValue);
        }

        private static int CalculateMinimumStackSize(PoE2ScoutItem item, decimal minValue)
        {
            if (item.ForceInclude)
            {
                return 1;
            }

            var itemValue = item.GetExaltedValue();
            if (itemValue <= 0)
            {
                return int.MaxValue;
            }

            var requiredStacks = decimal.Ceiling(minValue / itemValue);
            if (requiredStacks <= 1)
            {
                return 1;
            }

            if (requiredStacks >= int.MaxValue)
            {
                return int.MaxValue;
            }

            return (int)requiredStacks;
        }

        private static PoE2ScoutItem? FindFallbackMatch(IEnumerable<PoE2ScoutItem> items, string itemBaseName)
        {
            return items.FirstOrDefault(item =>
                item != null &&
                !string.IsNullOrEmpty(item.GetName()) &&
                itemBaseName.Contains(NormalizeApiItemNameForMatching(item.GetName()), StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeUniqueCategoryApiId(string? categoryApiId)
        {
            return categoryApiId?.Replace(" ", string.Empty).ToLowerInvariant() switch
            {
                "accessories" => "accessory",
                "accessory" => "accessory",
                "armours" => "armour",
                "armour" => "armour",
                "charms" => "charm",
                "charm" => "charm",
                "flasks" => "flask",
                "flask" => "flask",
                "idols" => "idol",
                "idol" => "idol",
                "jewels" => "jewel",
                "jewel" => "jewel",
                "maps" => "map",
                "map" => "map",
                "sanctumrelics" => "sanctum",
                "sanctumrelic" => "sanctum",
                "sanctumresearch" => "sanctum",
                "sanctum" => "sanctum",
                "weapons" => "weapon",
                "weapon" => "weapon",
                _ => categoryApiId?.Replace(" ", string.Empty).ToLowerInvariant() ?? string.Empty
            };
        }
        
        private string BuildApiUrl(string category, int page)
        {
            var encodedLeague = Uri.EscapeDataString(_leagueName);
            var encodedCategory = Uri.EscapeDataString(category);
            return $"{_baseUrl}/{_realm}/Leagues/{encodedLeague}/Currencies/ByCategory?Category={encodedCategory}&Page={page}&PerPage=250";
        }

        private string BuildUniqueApiUrl(string category, int page)
        {
            var encodedLeague = Uri.EscapeDataString(_leagueName);
            var encodedCategory = Uri.EscapeDataString(category);
            return $"{_baseUrl}/{_realm}/Leagues/{encodedLeague}/Uniques/ByCategory?Category={encodedCategory}&Page={page}&PerPage=250";
        }

        private string BuildItemCategoriesUrl()
        {
            var encodedLeague = Uri.EscapeDataString(_leagueName);
            return $"{_baseUrl}/{_realm}/Leagues/{encodedLeague}/Items/Categories";
        }
        
        private async Task ApplyRateLimit()
        {
            try
            {
                var timeSinceLastRequest = DateTime.Now - _lastRequestTime;
                if (timeSinceLastRequest < _requestDelay)
                {
                    var delayNeeded = _requestDelay - timeSinceLastRequest;
                    await Task.Delay(delayNeeded);
                }
            }
            catch (Exception ex)
            {
                _logError($"Error during rate limit delay: {ex.Message}");
                // still apply minimum delay in case of error
                await Task.Delay(1000); // 1 second fallback delay
            }
            finally
            {
                // always update last request time to ensure rate limit is maintained even if operations fail
                _lastRequestTime = DateTime.Now;
            }
        }
        
        private PoE2ScoutApiResponse? DeserializeApiResponse(string jsonResponse, int page)
        {
            try
            {
                if (page == 1)
                {
                    _logInfo($"received JSON response ({jsonResponse.Length} chars)");
                }
                
                var options = new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                
                var container = System.Text.Json.JsonSerializer.Deserialize<PoE2ScoutApiResponse>(jsonResponse, options);
                
                if (container == null)
                {
                    _logError($"failed to deserialize response for page {page}");
                    return null;
                }
                
                if (page == 1)
                {
                    _logInfo($"API response: {container.Pages} pages total, {container.Items?.Count ?? 0} items on page 1");
                }
                
                return container;
            }
            catch (Exception ex)
            {
                _logError($"error deserializing API response for page {page}: {ex.Message}");
                return null;
            }
        }
        
        private void LogDownloadProgress(int page, PoE2ScoutApiResponse container)
        {
            // only log every 5 pages or final page to reduce spam
            if (page == 1 || page % 5 == 0 || container.CurrentPage >= container.Pages)
            {
                _logInfo($"Downloaded page {page}/{container.Pages} with {container.Items.Count} items");
            }
        }
        
        private void LogInnerException(Exception ex)
        {
            if (ex.InnerException != null)
            {
                _logError($"Inner exception: {ex.InnerException.Message}");
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
