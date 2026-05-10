using BeeBAK.Ecommerce;
using BeeBAK.EntityFrameworkCore.Akakce;
using BeeBAK.EntityFrameworkCore.Cimri;
using BeeBAK.EntityFrameworkCore.Ecommerce;
using BeeBAK.EntityFrameworkCore.Shares;
using BeeBAK.Marketplaces.Akakce;
using BeeBAK.Marketplaces.Cimri;
using BeeBAK.Shares;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.AuditLogging.EntityFrameworkCore;
using Volo.Abp.BackgroundJobs.EntityFrameworkCore;
using Volo.Abp.BlobStoring.Database.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Volo.Abp.FeatureManagement.EntityFrameworkCore;
using Volo.Abp.Identity;
using Volo.Abp.Identity.EntityFrameworkCore;
using Volo.Abp.PermissionManagement.EntityFrameworkCore;
using Volo.Abp.SettingManagement.EntityFrameworkCore;
using Volo.Abp.OpenIddict.EntityFrameworkCore;
using Volo.Abp.TenantManagement;
using Volo.Abp.TenantManagement.EntityFrameworkCore;

namespace BeeBAK.EntityFrameworkCore;

[ReplaceDbContext(typeof(IIdentityDbContext))]
[ReplaceDbContext(typeof(ITenantManagementDbContext))]
[ConnectionStringName("Default")]
public class BeeBAKDbContext :
    AbpDbContext<BeeBAKDbContext>,
    ITenantManagementDbContext,
    IIdentityDbContext
{
    /* Add DbSet properties for your Aggregate Roots / Entities here. */

    public DbSet<EcMarketplaceCategory> EcMarketplaceCategories { get; set; }
    public DbSet<EcProduct> EcProducts { get; set; }
    public DbSet<EcProductDetail> EcProductDetails { get; set; }
    public DbSet<EcProductImage> EcProductImages { get; set; }
    public DbSet<EcProductPriceSnapshot> EcProductPriceSnapshots { get; set; }
    public DbSet<EcScrapeRun> EcScrapeRuns { get; set; }
    public DbSet<EcScrapeRunEvent> EcScrapeRunEvents { get; set; }

    public DbSet<CimriProduct> CimriProducts { get; set; }
    public DbSet<CimriOffer> CimriOffers { get; set; }
    public DbSet<CimriMerchant> CimriMerchants { get; set; }

    public DbSet<AkakceProduct> AkakceProducts { get; set; }
    public DbSet<AkakceOffer> AkakceOffers { get; set; }
    public DbSet<AkakceMerchant> AkakceMerchants { get; set; }

    public DbSet<BeebakShareProductDayBlock> BeebakShareProductDayBlocks { get; set; }
    public DbSet<BeebakShareCardLog> BeebakShareCardLogs { get; set; }

    #region Entities from the modules

    /* Notice: We only implemented IIdentityProDbContext and ISaasDbContext
     * and replaced them for this DbContext. This allows you to perform JOIN
     * queries for the entities of these modules over the repositories easily. You
     * typically don't need that for other modules. But, if you need, you can
     * implement the DbContext interface of the needed module and use ReplaceDbContext
     * attribute just like IIdentityProDbContext and ISaasDbContext.
     *
     * More info: Replacing a DbContext of a module ensures that the related module
     * uses this DbContext on runtime. Otherwise, it will use its own DbContext class.
     */

    // Identity
    public DbSet<IdentityUser> Users { get; set; }
    public DbSet<IdentityRole> Roles { get; set; }
    public DbSet<IdentityClaimType> ClaimTypes { get; set; }
    public DbSet<OrganizationUnit> OrganizationUnits { get; set; }
    public DbSet<IdentitySecurityLog> SecurityLogs { get; set; }
    public DbSet<IdentityLinkUser> LinkUsers { get; set; }
    public DbSet<IdentityUserDelegation> UserDelegations { get; set; }
    public DbSet<IdentitySession> Sessions { get; set; }

    // Tenant Management
    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<TenantConnectionString> TenantConnectionStrings { get; set; }

    #endregion

    public BeeBAKDbContext(DbContextOptions<BeeBAKDbContext> options)
        : base(options)
    {

    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        /* Include modules to your migration db context */

        builder.ConfigurePermissionManagement();
        builder.ConfigureSettingManagement();
        builder.ConfigureBackgroundJobs();
        builder.ConfigureAuditLogging();
        builder.ConfigureFeatureManagement();
        builder.ConfigureIdentity();
        builder.ConfigureOpenIddict();
        builder.ConfigureTenantManagement();
        builder.ConfigureBlobStoring();

        builder.ConfigureEcommerce();
        builder.ConfigureCimri();
        builder.ConfigureAkakce();
        builder.ConfigureBeebakShares();
    }
}
