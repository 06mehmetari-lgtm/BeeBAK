using BeeBAK.Marketplaces.Akakce;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace BeeBAK.EntityFrameworkCore.Akakce;

public static class AkakceDbContextModelCreatingExtensions
{
    public static void ConfigureAkakce(this ModelBuilder builder)
    {
        builder.Entity<AkakceMerchant>(b =>
        {
            b.ToTable(BeeBAKConsts.DbTablePrefix + "AkakceMerchants", BeeBAKConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Name).IsRequired().HasMaxLength(AkakceConsts.MaxMerchantNameLength);
            b.Property(x => x.Slug).IsRequired().HasMaxLength(AkakceConsts.MaxMerchantSlugLength);
            b.Property(x => x.LogoUrl).HasMaxLength(AkakceConsts.MaxMerchantLogoUrlLength);

            b.HasIndex(x => x.Slug).IsUnique();
        });

        builder.Entity<AkakceProduct>(b =>
        {
            b.ToTable(BeeBAKConsts.DbTablePrefix + "AkakceProducts", BeeBAKConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.ProductCode).IsRequired().HasMaxLength(AkakceConsts.MaxProductCodeLength);
            b.Property(x => x.ProductUrl).IsRequired().HasMaxLength(AkakceConsts.MaxProductUrlLength);
            b.Property(x => x.Title).IsRequired().HasMaxLength(AkakceConsts.MaxProductTitleLength);
            b.Property(x => x.BrandName).HasMaxLength(AkakceConsts.MaxBrandNameLength);
            b.Property(x => x.PrimaryImageUrl).HasMaxLength(AkakceConsts.MaxImageUrlLength);
            b.Property(x => x.CategoryPath).HasMaxLength(AkakceConsts.MaxCategoryPathLength);
            b.Property(x => x.DiscountPercent).HasPrecision(9, 4);
            b.Property(x => x.BestPriceAmount).HasPrecision(18, 4);
            b.Property(x => x.PreviousPriceAmount).HasPrecision(18, 4);

            b.HasIndex(x => x.ProductCode).IsUnique();
            b.HasIndex(x => x.LastSyncedUtc);

            b.HasMany(x => x.Offers)
                .WithOne(x => x.Product)
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AkakceOffer>(b =>
        {
            b.ToTable(BeeBAKConsts.DbTablePrefix + "AkakceOffers", BeeBAKConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Currency).IsRequired().HasMaxLength(8);
            b.Property(x => x.OfferTitle).HasMaxLength(AkakceConsts.MaxOfferTitleLength);
            b.Property(x => x.SellerName).HasMaxLength(AkakceConsts.MaxSellerNameLength);
            b.Property(x => x.ShippingText).HasMaxLength(AkakceConsts.MaxOfferTextLength);
            b.Property(x => x.ShippingAmount).HasPrecision(18, 4);
            b.Property(x => x.StockText).HasMaxLength(AkakceConsts.MaxOfferTextLength);
            b.Property(x => x.DeliveryText).HasMaxLength(AkakceConsts.MaxOfferTextLength);
            b.Property(x => x.LastUpdatedText).HasMaxLength(AkakceConsts.MaxOfferTextLength);
            b.Property(x => x.OfferUrl).HasMaxLength(AkakceConsts.MaxOfferUrlLength);
            b.Property(x => x.MerchantProductUrl).HasMaxLength(AkakceConsts.MaxMerchantProductUrlLength);
            b.Property(x => x.SiteRedirectUrl).HasMaxLength(AkakceConsts.MaxOfferUrlLength);
            b.Property(x => x.Price).HasPrecision(18, 4);

            b.HasIndex(x => new { x.ProductId, x.DisplayOrder });
            b.HasIndex(x => x.MerchantId);

            b.HasOne(x => x.Merchant)
                .WithMany()
                .HasForeignKey(x => x.MerchantId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
