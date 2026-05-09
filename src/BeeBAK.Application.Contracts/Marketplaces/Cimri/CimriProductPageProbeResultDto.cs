using System;

namespace BeeBAK.Marketplaces.Cimri;

public class CimriProductPageProbeResultDto
{
    public bool Success { get; set; }

    public string? Message { get; set; }

    public DateTime ProbedUtc { get; set; }

    public CimriProductDto? Product { get; set; }
}
