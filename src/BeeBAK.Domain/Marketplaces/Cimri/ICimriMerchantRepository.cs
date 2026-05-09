using System;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace BeeBAK.Marketplaces.Cimri;

public interface ICimriMerchantRepository : IRepository<CimriMerchant, Guid>
{
    Task<CimriMerchant?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default);
}
