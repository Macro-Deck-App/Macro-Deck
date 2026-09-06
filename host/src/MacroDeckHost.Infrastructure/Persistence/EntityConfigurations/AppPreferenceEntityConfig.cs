using MacroDeckHost.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MacroDeckHost.Infrastructure.Persistence.EntityConfigurations;

public class AppPreferenceEntityConfig : IEntityTypeConfiguration<AppPreferenceEntity>
{
	public void Configure(EntityTypeBuilder<AppPreferenceEntity> builder)
	{
		builder.ToTable("app_preference");

		builder.HasKey(x => x.Key);

		builder.Property(x => x.Key).HasColumnName("ap_key");
		builder.Property(x => x.Value).HasColumnName("ap_value");
		builder.Property(x => x.CreatedAt).HasColumnName("ap_created_at");
		builder.Property(x => x.UpdatedAt).HasColumnName("ap_updated_at");
	}
}
