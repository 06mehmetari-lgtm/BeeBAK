using System.Collections.Generic;

namespace BeeBAK.Marketplaces.Cimri;

public class CimriClientOptions
{
    public const string SectionName = "Cimri";

    /// <summary>Site origin, e.g. https://www.cimri.com</summary>
    public string BaseUrl { get; set; } = "https://www.cimri.com";

    /// <summary>İndirimli ürünler listeleme yolu.</summary>
    public string DiscountedListingPath { get; set; } = "/indirimli-urunler";

    /// <summary>Listeleme akışında en fazla okunacak sayfa sayısı (UI'dan gelmediğinde).</summary>
    public int DefaultMaxPages { get; set; } = 5;

    /// <summary>Veritabanına yazılacak ürün üst sınırı (UI'dan gelmediğinde).</summary>
    public int DefaultMaxProducts { get; set; } = 100;

    /// <summary>Selenium headless modu — false yapılırsa Chrome penceresi açılır (geliştirme/debug).</summary>
    public bool SeleniumHeadless { get; set; } = true;

    /// <summary>Sistemde yüklü Chrome yolu (boşsa PATH / Selenium Manager).</summary>
    public string? SeleniumChromeBinaryPath { get; set; }

    /// <summary>Kullanıcı temsilcisi başlığı.</summary>
    public string UserAgent { get; set; } =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36";

    /// <summary>İsteğe bağlı çerez başlığı (bot doğrulama atlatmak için).</summary>
    public string? Cookie { get; set; }

    /// <summary>Sayfa yüklemesinde hedef yapı belirene kadar maksimum bekleme (ms).</summary>
    public int PageLoadTimeoutMs { get; set; } = 60000;

    /// <summary>"Daha Fazla Ürün Göster" tıklandıktan sonra yeni ürün kartlarının yüklenmesi için bekleme (ms).</summary>
    public int LoadMoreClickPauseMs { get; set; } = 1500;

    /// <summary>"+9 Fiyat Teklifini Gör" tıklandıktan sonra ek tekliflerin DOM'a oturması için bekleme (ms).</summary>
    public int ExpandOffersClickPauseMs { get; set; } = 1200;

    /// <summary>Lazy-yüklü içerik için sayfa kaydırma adımı sayısı.</summary>
    public int ScrollPasses { get; set; } = 6;

    /// <summary>Kaydırma adımları arası bekleme (ms).</summary>
    public int ScrollPauseMs { get; set; } = 350;

    /// <summary>Detay sayfasında container yüklenene kadar CSS bekleme süresi (ms).</summary>
    public int DetailWaitTimeoutMs { get; set; } = 25000;

    /// <summary>Ürünler arası bekleme (rate limit / saygılı tarama).</summary>
    public int DelayBetweenProductsMs { get; set; } = 750;

    /// <summary>Detay sayfasından kaç teklif satırı maksimum yazılacağı (0 = sınırsız).</summary>
    public int MaxOffersPerProduct { get; set; } = 50;

    /// <summary>Ek Chromium bayrakları.</summary>
    public List<string>? ChromiumExtraArgs { get; set; }

    /// <summary>Remote Selenium Grid URL (örn. http://selenium-hub:4444/wd/hub). Boşsa lokal Chrome kullanılır.</summary>
    public string? SeleniumGridUrl { get; set; }

    /// <summary>Selenium grid komut zaman aşımı (ms).</summary>
    public int SeleniumCommandTimeoutMs { get; set; } = 120_000;

    /// <summary>Görsel/font/CSS isteklerini engellemek scraping'i ciddi şekilde hızlandırır.</summary>
    public bool BlockImages { get; set; } = true;
    public bool BlockFonts { get; set; } = true;
    public bool BlockStyles { get; set; } = false;

    /// <summary>Sayfa başına eklenebilecek küçük rastgele gecikmenin üst sınırı (ms). 0 = kapalı.</summary>
    public int RandomDelayJitterMs { get; set; } = 600;

    /// <summary>Bir ürünü tekrar scrape etmeden önce beklenecek süre (saniye). Redis dedup'ta kullanılır.</summary>
    public int DedupTtlSeconds { get; set; } = 6 * 60 * 60;

    /// <summary>Kullanıcı API'den senkronizasyon talep ettiğinde queue'ya iş atan mı yoksa in-process mi çalışsın?</summary>
    public bool UseQueue { get; set; } = true;

    /// <summary>
    /// Cimri'nin <c>/offer/{id}</c> redirect URL'lerini, PDP'yi açtığımız aynı Selenium oturumunda
    /// yeni tab açarak çözer. Cimri direkt HEAD/GET'e 403 dönüyor; tarayıcı oturumunda redirect chain
    /// güvenli şekilde takip ediliyor. Default: true.
    /// </summary>
    public bool ResolveOfferUrlsViaSelenium { get; set; } = true;

    /// <summary>
    /// Selenium tabanlı offer URL resolve sırasında her bir tab için bekleme üst sınırı (ms).
    /// Bu süre içinde cimri.com host'undan farklı bir host'a geçilirse sonuç o URL kabul edilir.
    /// </summary>
    public int OfferResolveTimeoutMs { get; set; } = 6000;

    /// <summary>
    /// Selenium ile tek bir ürün başına en fazla kaç offer URL'si çözülecek (perf cap).
    /// 0 = tüm offer'lar denenir.
    /// </summary>
    public int MaxOfferUrlsToResolveViaSelenium { get; set; } = 12;

    /// <summary>
    /// Selenium ile çözülmüş Cimri offer-id → gerçek mağaza URL'si Redis cache TTL süresi (saniye).
    /// Aynı offer farklı ürün listelerinde tekrarlanırsa Selenium tab açma adımı atlanır.
    /// </summary>
    public int OfferUrlCacheTtlSeconds { get; set; } = 7 * 24 * 60 * 60;

    public CimriTelegramNotificationOptions Telegram { get; set; } = new();
}

public class CimriTelegramNotificationOptions
{
    public string? BotToken { get; set; }

    public string? ChatId { get; set; }
}
