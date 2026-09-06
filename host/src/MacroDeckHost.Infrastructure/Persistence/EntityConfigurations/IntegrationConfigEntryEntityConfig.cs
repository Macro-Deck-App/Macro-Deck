using MacroDeckHost.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MacroDeckHost.Infrastructure.Persistence.EntityConfigurations;

public class IntegrationConfigEntryEntityConfig : BaseEntityConfig<IntegrationConfigEntryEntity>
{
	public IntegrationConfigEntryEntityConfig()
		: base("integration_config_entry", "ice_")
	{
	}

	public override void Configure(EntityTypeBuilder<IntegrationConfigEntryEntity> builder)
	{
		base.Configure(builder);

		builder.Property(x => x.IntegrationId).HasColumnName(ColumnPrefix + "integration_id");
		builder.Property(x => x.Title).HasColumnName(ColumnPrefix + "title");
		builder.Property(x => x.ValuesJson).HasColumnName(ColumnPrefix + "values_json");
		builder.Property(x => x.UpdatedAt).HasColumnName(ColumnPrefix + "updated_at");

		builder.HasIndex(x => x.IntegrationId);
	}
}
