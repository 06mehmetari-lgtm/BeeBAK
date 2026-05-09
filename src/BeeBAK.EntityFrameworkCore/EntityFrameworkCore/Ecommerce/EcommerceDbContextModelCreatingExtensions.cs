using BeeBAK.Ecommerce;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace BeeBAK.EntityFrameworkCore.Ecommerce;

public static class EcommerceDbContextModelCreatingExtensions
{
    public static void ConfigureEcommerce(this ModelBuilder builder)
    {
        builder.Entity<EcMarketplaceCategory>(b =>
        {
            b.ToTable(BeeBAKConsts.DbTablePrefix + "EcMarketplaceCategories", BeeBAKConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.ExtraAttributesJson).HasColumnType("jsonb");
            b.HasIndex(x => new { x.Marketplace, x.ExternalCategoryId }).IsUnique();
            b.HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<EcProduct>(b =>
        {
            b.ToTable(BeeBAKConsts.DbTablePrefix + "EcProducts", BeeBAKConsts.DbSchema);
            b.ConfigureByConvention();
            b.HasIndex(x => new { x.Marketplace, x.ExternalProductId }).IsUnique();
            b.HasOne(x => x.PrimaryCategory).WithMany(x => x.Products).HasForeignKey(x => x.PrimaryCategoryId)
                .OnDelete(DeleteBehavior.SetNull);
            b.HasMany(x => x.Images).WithOne(x => x.Product).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.PriceSnapshots).WithOne(x => x.Product).HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Detail).WithOne(x => x.Product).HasForeignKey<EcProductDetail>(x => x.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<EcProductDetail>(b =>
        {
            b.ToTable(BeeBAKConsts.DbTablePrefix + "EcProductDetails", BeeBAKConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.SpecificationsJson).HasColumnType("jsonb");
            b.Property(x => x.SellerScoreJson).HasColumnType("jsonb");
            b.Property(x => x.RatingAverage).HasPrecision(6, 2);
        });

        builder.Entity<EcProductImage>(b =>
        {
            b.ToTable(BeeBAKConsts.DbTablePrefix + "EcProductImages", BeeBAKConsts.DbSchema);
            b.ConfigureByConvention();
        });

        builder.Entity<EcProductPriceSnapshot>(b =>
        {
            b.ToTable(BeeBAKConsts.DbTablePrefix + "EcProductPriceSnapshots", BeeBAKConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.PriceAmount).HasPrecision(18, 4);
            b.Property(x => x.ListPriceAmount).HasPrecision(18, 4);
            b.Property(x => x.DiscountPercent).HasPrecision(9, 4);
            b.Property(x => x.RawOfferJson).HasColumnType("jsonb");
        });

        builder.Entity<EcScrapeRun>(b =>
        {
            b.ToTable(BeeBAKConsts.DbTablePrefix + "EcScrapeRuns", BeeBAKConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.StatisticsJson).HasColumnType("jsonb");
        });

        builder.Entity<EcScrapeRunEvent>(b =>
        {
            b.ToTable(BeeBAKConsts.DbTablePrefix + "EcScrapeRunEvents", BeeBAKConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Phase).IsRequired().HasMaxLength(64);
            b.Property(x => x.Message).IsRequired().HasMaxLength(1024);
            b.Property(x => x.Title).HasMaxLength(512);
            b.Property(x => x.Url).HasMaxLength(2048);
            b.HasIndex(x => new { x.ScrapeRunId, x.TimestampUtc });
        });
    }
}
