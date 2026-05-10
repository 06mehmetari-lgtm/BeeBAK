namespace BeeBAK.Shares;

public class ShareDeckBuildInput
{
    /// <summary>Kart başına en fazla ürün (1–4).</summary>
    public int MaxSlotsPerCard { get; set; } = 4;

    /// <summary>Toplam en fazla kaç ürün kartlara dağıtılsın.</summary>
    public int MaxProductsTotal { get; set; } = 32;

    public string ChannelName { get; set; } = BeebakShareConsts.DefaultChannelName;
}
