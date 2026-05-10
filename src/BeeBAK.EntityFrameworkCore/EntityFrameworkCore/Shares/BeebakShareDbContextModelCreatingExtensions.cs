using BeeBAK.Shares;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace BeeBAK.EntityFrameworkCore.Shares;

public static class BeebakShareDbContextModelCreatingExtensions
{
    public static void ConfigureBeebakShares(this ModelBuilder builder)
    {
        builder.Entity<BeebakShareProductDayBlock>(b =>
        {
            b.ToTable(BeeBAKConsts.DbTablePrefix + "BeebakShareProductDayBlocks", BeeBAKConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.CimriContentId).IsRequired().HasMaxLength(BeebakShareConsts.MaxCimriContentIdLength);
            b.Property(x => x.ChannelName).IsRequired().HasMaxLength(64);
            b.Property(x => x.BlockUtcDate).HasColumnType("date");
            b.HasIndex(x => new { x.CimriContentId, x.BlockUtcDate, x.ChannelName }).IsUnique();
        });

        builder.Entity<BeebakShareCardLog>(b =>
        {
            b.ToTable(BeeBAKConsts.DbTablePrefix + "BeebakShareCardLogs", BeeBAKConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.ChannelName).IsRequired().HasMaxLength(64);
            b.Property(x => x.CardPayloadJson).IsRequired().HasColumnType("jsonb");
            b.Property(x => x.ProductFingerprint).IsRequired().HasMaxLength(BeebakShareConsts.MaxFingerprintLength);
            b.HasIndex(x => new { x.CreatedUtc, x.ProductFingerprint });
        });
    }
}
