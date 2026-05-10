using System;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace BeeBAK.Marketplaces.Akakce;

public interface IAkakceMerchantRepository : IRepository<AkakceMerchant, Guid>
{
    Task<AkakceMerchant?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default);
}
