using System.Collections.Generic;

namespace BeeBAK.Marketplaces.Akakce;

public class AkakceClientOptions
{
    public const string SectionName = "Akakce";

    public string BaseUrl { get; set; } = "https://www.akakce.com";
    public string? ListingPageUrl { get; set; } = "https://www.akakce.com/fiyati-dusen-urunler/?s=5";
    public string? AllowedAkakceHostSuffix { get; set; } = "akakce.com";
    public int DefaultMaxPages { get; set; } = 5;
    public int DefaultMaxProducts { get; set; } = 100;
    public bool SeleniumHeadless { get; set; } = true;
    public string? SeleniumChromeBinaryPath { get; set; }
    public string? SeleniumGridUrl { get; set; }
    public int SeleniumCommandTimeoutMs { get; set; } = 120_000;
    public string UserAgent { get; set; } =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36";
    public string? Cookie { get; set; }
    public int PageLoadTimeoutMs { get; set; } = 60_000;
    public int ListingWaitTimeoutMs { get; set; } = 25_000;
    public int DetailWaitTimeoutMs { get; set; } = 25_000;
    public int ScrollPasses { get; set; } = 2;
    public int ScrollPauseMs { get; set; } = 250;
    public int DelayBetweenProductsMs { get; set; } = 0;
    public int MaxOffersPerProduct { get; set; } = 50;
    public bool BlockImages { get; set; } = true;
    public bool BlockFonts { get; set; } = true;
    public bool BlockStyles { get; set; }
    public int DedupTtlSeconds { get; set; } = 6 * 60 * 60;
    public bool UseQueue { get; set; } = true;
    public int ProductDetailEnqueueBatchSize { get; set; } = 20;
    public List<string>? ChromiumExtraArgs { get; set; }
}
