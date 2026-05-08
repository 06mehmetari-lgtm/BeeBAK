using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BeeBAK.Data;
using Volo.Abp.DependencyInjection;

namespace BeeBAK.EntityFrameworkCore;

public class EntityFrameworkCoreBeeBAKDbSchemaMigrator
    : IBeeBAKDbSchemaMigrator, ITransientDependency
{
    private readonly IServiceProvider _serviceProvider;

    public EntityFrameworkCoreBeeBAKDbSchemaMigrator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task MigrateAsync()
    {
        /* We intentionally resolving the BeeBAKDbContext
         * from IServiceProvider (instead of directly injecting it)
         * to properly get the connection string of the current tenant in the
         * current scope.
         */

        await _serviceProvider
            .GetRequiredService<BeeBAKDbContext>()
            .Database
            .MigrateAsync();
    }
}
