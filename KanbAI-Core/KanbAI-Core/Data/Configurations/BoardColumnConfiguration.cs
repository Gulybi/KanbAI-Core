using KanbAI_Core.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KanbAI_Core.Data.Configurations;

public class BoardColumnConfiguration : IEntityTypeConfiguration<BoardColumn>
{
    public void Configure(EntityTypeBuilder<BoardColumn> builder)
    {
        builder.HasKey(bc => bc.Id);

        builder.Property(bc => bc.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(bc => bc.ColorCode)
            .HasMaxLength(20);

        builder.Property(bc => bc.ColumnOrder)
            .IsRequired();

        builder.HasOne(bc => bc.Project)
            .WithMany(p => p.Columns)
            .HasForeignKey(bc => bc.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
