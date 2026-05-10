using System.Collections.Generic;

namespace BeeBAK.Shares;

public class BeebakShareCardSlotDto
{
    /// <summary>1..4 kart üzerinde gösterilecek sıra.</summary>
    public int SlotIndex { get; set; }

    public string ContentId { get; set; } = "";

    public string Title { get; set; } = "";

    public string? ImageUrl { get; set; }

    public decimal? DiscountPercent { get; set; }

    /// <summary>En ucuz üç farklı mağaza (varsa).</summary>
    public List<BeebakShareLinkDto> TopMerchantLinks { get; set; } = new();
}
