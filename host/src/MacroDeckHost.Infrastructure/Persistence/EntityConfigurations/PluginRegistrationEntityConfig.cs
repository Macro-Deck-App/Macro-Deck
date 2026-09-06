using MacroDeckHost.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MacroDeckHost.Infrastructure.Persistence.EntityConfigurations;

public class PluginRegistrationEntityConfig : BaseEntityConfig<PluginRegistrationEntity>
{
	public PluginRegistrationEntityConfig()
		: base("plugin_registration", "pr_")
	{
	}

	public override void Configure(EntityTypeBuilder<PluginRegistrationEntity> builder)
	{
		base.Configure(builder);

		builder.Property(x => x.PluginId).HasColumnName(ColumnPrefix + "plugin_id");
		builder.Property(x => x.DisplayName).HasColumnName(ColumnPrefix + "display_name");
		builder.Property(x => x.SecretHash).HasColumnName(ColumnPrefix + "secret_hash");
		builder.Property(x => x.AccessTokenId).HasColumnName(ColumnPrefix + "access_token_id");
		builder.Property(x => x.Origin).HasColumnName(ColumnPrefix + "origin");
		builder.Property(x => x.LastSeenAt).HasColumnName(ColumnPrefix + "last_seen_at");
		builder.Property(x => x.RevokedAt).HasColumnName(ColumnPrefix + "revoked_at");

		builder.HasIndex(x => x.PluginId).IsUnique();
		builder.HasIndex(x => x.AccessTokenId);
	}
}
