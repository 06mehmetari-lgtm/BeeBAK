namespace BeeBAK.Shares;

public class BeebakShareLinkDto
{
    public string MerchantName { get; set; } = "";

    public decimal Price { get; set; }

    public string Currency { get; set; } = "TRY";

    /// <summary>Mağaza PDP veya Cimri teklif linki.</summary>
    public string Url { get; set; } = "";
}
