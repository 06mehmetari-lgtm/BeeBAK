using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BeeBAK.Ecommerce;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Uow;

namespace BeeBAK.Data;

/// <summary>
/// Varsayılan Trendyol üst segment kodları — tablo boşsa eklenir.
/// Yeni kodlar için doğrudan AppEcTrendyolNavSectionTrackers tablosuna kayıt ekleyebilirsiniz.
/// </summary>
public class TrendyolNavSectionTrackerDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IRepository<EcTrendyolNavSectionTracker, Guid> _repository;
    private readonly IGuidGenerator _guidGenerator;

    /// <summary>Sıra: mobil alt menüdeki tipik sıraya yakın.</summary>
    private static readonly IReadOnlyList<(string ExternalCategoryId, int SortOrder)> Defaults =
    [
        ("1", 10),
        ("2", 20),
        ("3", 30),
        ("12", 40),
        ("16", 50),
        ("11", 60),
        ("9", 70),
        ("5", 80),
        ("10", 90),
        ("22", 100),
        ("flas-indirimler", 110),
        ("cok-satanlar:w1", 120),
    ];

    public TrendyolNavSectionTrackerDataSeedContributor(
        IRepository<EcTrendyolNavSectionTracker, Guid> repository,
        IGuidGenerator guidGenerator)
    {
        _repository = repository;
        _guidGenerator = guidGenerator;
    }

    [UnitOfWork]
    public async Task SeedAsync(DataSeedContext context)
    {
        if (await _repository.GetCountAsync() > 0)
        {
            return;
        }

        foreach (var (externalCategoryId, sortOrder) in Defaults)
        {
            await _repository.InsertAsync(
                new EcTrendyolNavSectionTracker(_guidGenerator.Create(), externalCategoryId, sortOrder),
                autoSave: true);
        }
    }
}
