using KanbAI_Core.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KanbAI_Core.Data.Configurations;

public class KanbanTaskConfiguration : IEntityTypeConfiguration<KanbanTask>
{
    public void Configure(EntityTypeBuilder<KanbanTask> builder)
    {
        builder.HasKey(kt => kt.Id);

        builder.Property(kt => kt.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(kt => kt.Content);

        builder.Property(kt => kt.TaskOrder)
            .IsRequired();

        builder.HasOne(kt => kt.Column)
            .WithMany(bc => bc.Tasks)
            .HasForeignKey(kt => kt.ColumnId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(kt => kt.AssignedUser)
            .WithMany(u => u.AssignedTasks)
            .HasForeignKey(kt => kt.AssignedId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
