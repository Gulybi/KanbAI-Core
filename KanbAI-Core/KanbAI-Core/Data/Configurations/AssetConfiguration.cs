using KanbAI_Core.Models.Entities;
using KanbAI_Core.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KanbAI_Core.Data.Configurations;

public class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.FileName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(a => a.StorageKey)
            .IsRequired()
            .HasMaxLength(1024);

        builder.HasIndex(a => a.StorageKey)
            .IsUnique();

        builder.Property(a => a.ThumbnailKey)
            .HasMaxLength(1024);

        builder.Property(a => a.MimeType)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(a => a.FileSize)
            .IsRequired();

        builder.Property(a => a.ProcessingStatus)
            .IsRequired()
            .HasDefaultValue(ProcessingStatus.Pending);

        builder.HasOne(a => a.KanbanTask)
            .WithMany(kt => kt.Assets)
            .HasForeignKey(a => a.KanbanTaskId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
