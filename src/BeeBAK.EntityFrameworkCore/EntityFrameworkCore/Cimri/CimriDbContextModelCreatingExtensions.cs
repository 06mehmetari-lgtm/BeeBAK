using BeeBAK.Marketplaces.Cimri;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace BeeBAK.EntityFrameworkCore.Cimri;

public static class CimriDbContextModelCreatingExtensions
{
    public static void ConfigureCimri(this ModelBuilder builder)
    {
        builder.Entity<CimriMerchant>(b =>
        {
            b.ToTable(BeeBAKConsts.DbTablePrefix + "CimriMerchants", BeeBAKConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Name).IsRequired().HasMaxLength(CimriConsts.MaxMerchantNameLength);
            b.Property(x => x.Slug).IsRequired().HasMaxLength(CimriConsts.MaxMerchantSlugLength);
            b.Property(x => x.LogoUrl).HasMaxLength(CimriConsts.MaxMerchantLogoUrlLength);
            b.Property(x => x.ExternalMerchantId).HasMaxLength(64);

            b.HasIndex(x => x.Slug).IsUnique();
        });

        builder.Entity<CimriProduct>(b =>
        {
            b.ToTable(BeeBAKConsts.DbTablePrefix + "CimriProducts", BeeBAKConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.ContentId).IsRequired().HasMaxLength(CimriConsts.MaxContentIdLength);
            b.Property(x => x.ProductUrl).IsRequired().HasMaxLength(CimriConsts.MaxProductUrlLength);
            b.Property(x => x.PrimaryCategorySlug).HasMaxLength(160);
            b.Property(x => x.CategoryPath).HasMaxLength(CimriConsts.MaxCategoryPathLength);
            b.Property(x => x.Title).IsRequired().HasMaxLength(CimriConsts.MaxProductTitleLength);
            b.Property(x => x.BrandName).HasMaxLength(CimriConsts.MaxBrandNameLength);
            b.Property(x => x.PrimaryImageUrl).HasMaxLength(CimriConsts.MaxImageUrlLength);
            b.Property(x => x.BestPriceMerchantName).HasMaxLength(CimriConsts.MaxMerchantNameLength);

            b.Property(x => x.DiscountPercent).HasPrecision(9, 4);
            b.Property(x => x.BestPriceAmount).HasPrecision(18, 4);
            b.Property(x => x.PreviousPriceAmount).HasPrecision(18, 4);

            b.HasIndex(x => x.ContentId).IsUnique();
            b.HasIndex(x => x.PrimaryCategorySlug);
            b.HasIndex(x => x.LastSyncedUtc);

            b.HasMany(x => x.Offers)
                .WithOne(x => x.Product)
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CimriOffer>(b =>
        {
            b.ToTable(BeeBAKConsts.DbTablePrefix + "CimriOffers", BeeBAKConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Currency).IsRequired().HasMaxLength(8);
            b.Property(x => x.OfferTitle).HasMaxLength(CimriConsts.MaxOfferTitleLength);
            b.Property(x => x.SellerName).HasMaxLength(CimriConsts.MaxSellerNameLength);
            b.Property(x => x.ShippingText).HasMaxLength(CimriConsts.MaxOfferTextLength);
            b.Property(x => x.PromotionText).HasMaxLength(CimriConsts.MaxOfferTextLength);
            b.Property(x => x.LastUpdatedText).HasMaxLength(CimriConsts.MaxOfferTextLength);
            b.Property(x => x.InstallmentBadge).HasMaxLength(CimriConsts.MaxBadgeLength);
            b.Property(x => x.OfferUrl).HasMaxLength(CimriConsts.MaxOfferUrlLength);
            b.Property(x => x.MerchantProductUrl).HasMaxLength(CimriConsts.MaxMerchantProductUrlLength);
            b.Property(x => x.MerchantProductId).HasMaxLength(CimriConsts.MaxMerchantProductIdLength);

            b.Property(x => x.Price).HasPrecision(18, 4);
            b.Property(x => x.MerchantScore).HasPrecision(6, 2);

            b.HasIndex(x => new { x.ProductId, x.DisplayOrder });
            b.HasIndex(x => x.MerchantId);
            b.HasIndex(x => x.MerchantProductId);

            b.HasOne(x => x.Merchant)
                .WithMany()
                .HasForeignKey(x => x.MerchantId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
