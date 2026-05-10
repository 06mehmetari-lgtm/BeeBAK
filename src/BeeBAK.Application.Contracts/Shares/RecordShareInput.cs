using System;
using System.Collections.Generic;

namespace BeeBAK.Shares;

public class RecordShareInput
{
    public Guid CardId { get; set; }

    /// <summary>Karttaki ürün içerik kimlikleri (sıra korunur).</summary>
    public List<string> CimriContentIds { get; set; } = new();

    /// <summary>İstemciden gelen kart JSON (denetim / Telegram için).</summary>
    public string? CardPayloadJson { get; set; }

    public string ChannelName { get; set; } = BeebakShareConsts.DefaultChannelName;
}
