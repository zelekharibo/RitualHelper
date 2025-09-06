using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using ExileCore2.Shared;

namespace RitualHelper
{
    public class PoE2ScoutApiService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl = "https://poe2scout.com/api";
        private readonly string _leagueName;
        private readonly Action<string> _logInfo;
        private readonly Action<string> _logError;
        
        private DateTime _lastFetch = DateTime.MinValue;
        private List<PoE2ScoutItem> _cachedCurrency = new();
        private List<PoE2ScoutItem> _cachedOmens = new();
        private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(5);
        private DateTime _lastRequestTime = DateTime.MinValue;
        private readonly TimeSpan _requestDelay = TimeSpan.FromMilliseconds(500);

        public PoE2ScoutApiService(string leagueName = "Rise of the Abyssal", 
            Action<string>? logInfo = null, Action<string>? logError = null)
        {
            _logInfo = logInfo ?? (msg => Console.WriteLine($"RitualHelper: {msg}"));
            _logError = logError ?? (msg => Console.WriteLine($"RitualHelper ERROR: {msg}"));
            _leagueName = leagueName;
            
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            
            SetupUserAgent();
            _logInfo($"API Service initialized for League: {leagueName}");
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
                _logInfo("Fetching currency data from API...");
                _cachedCurrency = await LoadPagedDataFromApi("currency") ?? new List<PoE2ScoutItem>();
                
                if (_cachedCurrency.Any())
                {
                    _lastFetch = DateTime.Now;
                    _logInfo($"Successfully fetched {_cachedCurrency.Count} currency items");
                }
                else
                {
                    _logInfo("No currency items found");
                }
                
                return _cachedCurrency;
            }
            catch (Exception ex)
            {
                _logError($"Error fetching currency data: {ex.Message}");
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
                _logInfo("Fetching ritual omen data from API...");
                _cachedOmens = await LoadPagedDataFromApi("ritual") ?? new List<PoE2ScoutItem>();
                
                if (_cachedOmens.Any())
                {
                    _lastFetch = DateTime.Now;
                    _logInfo($"Successfully fetched {_cachedOmens.Count} ritual omens");
                }
                else
                {
                    _logInfo("No ritual omens found");
                }
                
                return _cachedOmens;
            }
            catch (Exception ex)
            {
                _logError($"Error fetching ritual omen data: {ex.Message}");
                LogInnerException(ex);
                return _cachedOmens; // return cached data on error
            }
        }

        public async Task<List<DeferItem>> GenerateDeferListAsync(decimal minExaltedValue = 0.1m)
        {
            var deferItems = new List<DeferItem>();

            try
            {
                // fetch and filter valuable items
                var currencyData = await GetCurrencyDataAsync();
                var omenData = await GetRitualOmensAsync();
                
                var valuableCurrency = FilterValuableItems(currencyData, minExaltedValue);
                var valuableOmens = FilterValuableItems(omenData, minExaltedValue);

                // convert to defer items with API prefix
                AddItemsToDefer(deferItems, valuableCurrency);
                AddItemsToDefer(deferItems, valuableOmens);

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
                    break;
                }
                
                page++;
            } while (container.CurrentPage < container.Pages);
            
            _logInfo($"Finished downloading {category}: {items.Count} total items");
            return items;
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
                .Where(item => item?.GetExaltedValue() >= minValue)
                .OrderByDescending(item => item.GetExaltedValue())
                .ToList();
        }
        
        private static void AddItemsToDefer(List<DeferItem> deferItems, List<PoE2ScoutItem> apiItems)
        {
            foreach (var item in apiItems.Where(i => i != null && !string.IsNullOrEmpty(i.GetName())))
            {
                var priority = CalculatePriority(item.GetExaltedValue());
                deferItems.Add(new DeferItem(item.GetName(), priority, 1, true));
            }
        }
        
        private string BuildApiUrl(string category, int page)
        {
            var encodedLeague = Uri.EscapeDataString(_leagueName);
            return $"{_baseUrl}/items/currency/{category}?league={encodedLeague}&page={page}&perPage=250";
        }
        
        private async Task ApplyRateLimit()
        {
            var timeSinceLastRequest = DateTime.Now - _lastRequestTime;
            if (timeSinceLastRequest < _requestDelay)
            {
                var delayNeeded = _requestDelay - timeSinceLastRequest;
                await Task.Delay(delayNeeded);
            }
            _lastRequestTime = DateTime.Now;
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
