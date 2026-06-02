using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RitualHelper
{
    public class PoE2ScoutItem
    {
        [JsonPropertyName("id")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int Id { get; set; }
        
        [JsonPropertyName("itemId")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int ItemId { get; set; }
        
        [JsonPropertyName("currencyCategoryId")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int CurrencyCategoryId { get; set; }

        [JsonPropertyName("currencyCategory")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int CurrencyCategory
        {
            get => CurrencyCategoryId;
            set => CurrencyCategoryId = value;
        }
        
        [JsonPropertyName("apiId")]
        public string ApiId { get; set; } = string.Empty;
        
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;
        
        [JsonPropertyName("categoryApiId")]
        public string CategoryApiId { get; set; } = string.Empty;
        
        [JsonPropertyName("iconUrl")]
        public string IconUrl { get; set; } = string.Empty;
        
        [JsonPropertyName("currentPrice")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public decimal CurrentPrice { get; set; }
        
        [JsonPropertyName("priceLogs")]
        public List<object> PriceLogs { get; set; } = new();

        [JsonIgnore]
        public bool ForceInclude { get; set; }

        public decimal GetExaltedValue()
        {
            return CurrentPrice;
        }

        public string GetName()
        {
            if (!string.IsNullOrWhiteSpace(Name))
            {
                return Name;
            }

            return Text ?? string.Empty;
        }
    }

    public class PoE2ScoutApiResponse
    {
        [JsonPropertyName("currentPage")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int CurrentPage { get; set; }
        
        [JsonPropertyName("pages")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int Pages { get; set; }
        
        [JsonPropertyName("total")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int Total { get; set; }
        
        [JsonPropertyName("items")]
        public List<PoE2ScoutItem> Items { get; set; } = new();
    }

    public class Category
    {
        [JsonPropertyName("id")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int Id { get; set; }
        
        [JsonPropertyName("apiId")]
        public string ApiId { get; set; } = string.Empty;
        
        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;
        
        [JsonPropertyName("icon")]
        public string Icon { get; set; } = string.Empty;
    }

    public class CategoriesResponse
    {
        [JsonPropertyName("unique_categories")]
        public List<Category> UniqueCategories { get; set; } = new();
        
        [JsonPropertyName("currency_categories")]
        public List<Category> CurrencyCategories { get; set; } = new();
    }
}
