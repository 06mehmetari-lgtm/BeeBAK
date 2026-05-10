using System;
using System.Collections.Generic;

namespace BeeBAK.Shares;

public class BeebakShareDeckDto
{
    public List<BeebakShareCardDto> Cards { get; set; } = new();

    /// <summary>Bugün daha önce paylaşıldığı için elenen ürün sayısı.</summary>
    public int SkippedAlreadySharedTodayCount { get; set; }

    /// <summary>Aday havuzundan çekilen ham ürün sayısı (filtre öncesi).</summary>
    public int CandidatePoolCount { get; set; }

    public DateTime GeneratedUtc { get; set; }
}
