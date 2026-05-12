using System.Collections.Generic;
using System.Linq;

namespace BeeBAK.Marketplaces.Cimri;

public class CimriClientOptions
{
    public const string SectionName = "Cimri";

    /// <summary>Site kök adresi — yapılandırmada verilir; kod içinde varsayılan yok.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>Eski yapılandırma alanı; listeleme adresi artık <see cref="ListingPageUrl"/> veya API ile verilir.</summary>
    public string DiscountedListingPath { get; set; } = "";

    /// <summary>
    /// Sunucu varsayılanı: Selenium ile açılacak tam HTTPS listeleme URL'si (ör. indirimli ürün veya kategori sayfası).
    /// </summary>
    public string? ListingPageUrl { get; set; }

    /// <summary>
    /// İzin verilen Cimri ana makinesi soneki (ör. cimri.com). Boşsa <see cref="BaseUrl"/> ana makinesi kullanılır.
    /// </summary>
    public string? AllowedCimriHostSuffix { get; set; }

    /// <summary>
    /// Yalnızca <see cref="AllowedMerchantNameSubstrings"/> ile eşleşen mağaza tekliflerini kaydet (LIKE alt-dize).
    /// </summary>
    public bool RestrictOffersToAllowedMerchants { get; set; } = true;

    /// <summary>Kayıtta tutulacak teklifler için mağaza ürün kimliği zorunlu.</summary>
    public bool RequireMerchantProductId { get; set; } = true;

    /// <summary>Filtre sonrası uygun teklif kalmazsa ürün satırını oluşturma / varsa sil.</summary>
    public bool SkipProductWithoutQualifiedOffers { get; set; } = true;

    /// <summary>Boşsa <see cref="CimriDefaultAllowedMerchants"/> kullanılır.</summary>
    public List<string> AllowedMerchantNameSubstrings { get; set; } =
        CimriDefaultAllowedMerchants.Substrings.ToList();

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
    /// Discovery'nin ürün detaylarını tek tek yerine her RabbitMQ mesajında kaç ürünle gruplayacağı (1 = eski davranış).
    /// İptal sonrası kuyrukta bekleyen mesaj sayısını düşürür.
    /// </summary>
    public int ProductDetailEnqueueBatchSize { get; set; } = 20;

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

    public CimriAutoSyncOptions AutoSync { get; set; } = new();

    public CimriPriceWatchOptions PriceWatch { get; set; } = new();

    public CimriPublishOptions Publish { get; set; } = new();
}

public class CimriAutoSyncOptions
{
    /// <summary>Worker 3 saatte bir otomatik tarama yapar.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Tarama tekrarı saat cinsinden (varsayılan 3). CategoryUrls varsa CategoryIntervalMinutes kullanılır.</summary>
    public int IntervalHours { get; set; } = 3;

    /// <summary>Kategori listesi yapılandırıldığında timer periyodu (dk). 0 = IntervalHours'a düşer.</summary>
    public int CategoryIntervalMinutes { get; set; } = 30;

    /// <summary>Worker başladığında ilk taramayı beklemeden hemen başlat.</summary>
    public bool RunOnStart { get; set; } = false;

    /// <summary>Taranacak tam URL (boşsa CimriClientOptions.ListingPageUrl kullanılır).</summary>
    public string? ListingPageUrl { get; set; }

    /// <summary>
    /// Kategori URL listesi. Dolu ise her tur bir sonraki URL seçilir (Redis sayacıyla round-robin).
    /// Boşsa ListingPageUrl / CimriClientOptions.ListingPageUrl kullanılır.
    /// </summary>
    public List<string> CategoryUrls { get; set; } = new();

    /// <summary>Tarama başına maksimum sayfa sayısı.</summary>
    public int MaxPages { get; set; } = 15;

    /// <summary>Tarama başına maksimum ürün sayısı (sayfada 200–1000 arası değişebilir).</summary>
    public int MaxProducts { get; set; } = 1000;
}

public class CimriPriceWatchOptions
{
    /// <summary>Son N günde eklenen ürünlerin fiyatını yeniden kontrol et.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Kaç saatte bir fiyat kontrolü yapılacağı.</summary>
    public int IntervalMinutes { get; set; } = 120;

    /// <summary>Worker başladığında hemen ilk kontrolü başlat.</summary>
    public bool RunOnStart { get; set; } = false;

    /// <summary>En fazla kaç günlük ürün taransın.</summary>
    public int ProductAgeDays { get; set; } = 2;

    /// <summary>Son taramadan bu kadar dk geçmemişse ürünü atla (dedup koruması).</summary>
    public int ResyncAfterMinutes { get; set; } = 90;

    /// <summary>Bir tur'da en fazla kaç ürün kuyruğa atılır.</summary>
    public int MaxProductsPerRun { get; set; } = 200;
}

public class CimriPublishOptions
{
    /// <summary>Telegram kuyruğundan gönderimler için minimum bekleme (dk).</summary>
    public int MinDelayMinutes { get; set; } = 5;

    /// <summary>Telegram kuyruğundan gönderimler için maksimum bekleme (dk).</summary>
    public int MaxDelayMinutes { get; set; } = 15;

    /// <summary>Bu indirim yüzdesinin altındaki ürünler kuyruğa alınmaz.</summary>
    public decimal MinDiscountPercent { get; set; } = 25m;

    /// <summary>Sessiz saat başlangıcı (İstanbul, dahil). Örn. 2 = 02:00.</summary>
    public int QuietStartHour { get; set; } = 2;

    /// <summary>Sessiz saat bitişi (İstanbul, hariç). Örn. 8 = 08:00.</summary>
    public int QuietEndHour { get; set; } = 8;
}

public class CimriTelegramNotificationOptions
{
    public string? BotToken { get; set; }

    public string? ChatId { get; set; }

    /// <summary>Ürün veritabanına yazıldığında Telegram'a kart mesajı gönder (bot token + chat id doluysa).</summary>
    public bool ShareProductCardsOnIngest { get; set; } = true;

    /// <summary>Aynı ürün için bu süre içinde tekrar Telegram gönderilmez (spam önleme).</summary>
    public int TelegramCardDedupHours { get; set; } = 6;

    /// <summary>Telegram kartı için gereken minimum indirim yüzdesi.</summary>
    public decimal MinDiscountPercentForTelegram { get; set; } = 50m;
}
