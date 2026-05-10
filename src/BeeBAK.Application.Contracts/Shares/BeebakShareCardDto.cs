using System;
using System.Collections.Generic;

namespace BeeBAK.Shares;

public class BeebakShareCardDto
{
    public Guid CardId { get; set; }

    public string ChannelName { get; set; } = BeebakShareConsts.DefaultChannelName;

    /// <summary>Görsel tema anahtarı (ember, aurora, tide, citrus).</summary>
    public string Theme { get; set; } = "ember";

    public string Headline { get; set; } = "";

    public string Tagline { get; set; } = "";

    public List<BeebakShareCardSlotDto> Slots { get; set; } = new();
}
