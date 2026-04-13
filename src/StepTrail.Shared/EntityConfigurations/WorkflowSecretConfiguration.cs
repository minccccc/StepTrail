using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StepTrail.Shared.Entities;

namespace StepTrail.Shared.EntityConfigurations;

public class WorkflowSecretConfiguration : IEntityTypeConfiguration<WorkflowSecret>
{
    public void Configure(EntityTypeBuilder<WorkflowSecret> builder)
    {
        builder.ToTable("workflow_secrets");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(s => s.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(s => s.Value).HasColumnName("value").IsRequired();
        builder.Property(s => s.Description).HasColumnName("description").HasMaxLength(1000);
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(s => s.Name).IsUnique();
    }
}
