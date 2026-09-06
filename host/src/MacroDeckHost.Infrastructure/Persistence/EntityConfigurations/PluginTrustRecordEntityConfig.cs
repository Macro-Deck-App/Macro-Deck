using MacroDeckHost.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MacroDeckHost.Infrastructure.Persistence.EntityConfigurations;

public class PluginTrustRecordEntityConfig : BaseEntityConfig<PluginTrustRecordEntity>
{
	public PluginTrustRecordEntityConfig()
		: base("plugin_trust_record", "ptr_")
	{
	}

	public override void Configure(EntityTypeBuilder<PluginTrustRecordEntity> builder)
	{
		base.Configure(builder);

		builder.Property(x => x.PluginId).HasColumnName(ColumnPrefix + "plugin_id");
		builder.Property(x => x.Version).HasColumnName(ColumnPrefix + "version");
		builder.Property(x => x.AdmittedVerdict).HasColumnName(ColumnPrefix + "admitted_verdict");
		builder.Property(x => x.CertificateId).HasColumnName(ColumnPrefix + "certificate_id");
		builder.Property(x => x.InstalledAt).HasColumnName(ColumnPrefix + "installed_at");

		builder.HasIndex(x => new { x.PluginId, x.Version }).IsUnique();
	}
}
