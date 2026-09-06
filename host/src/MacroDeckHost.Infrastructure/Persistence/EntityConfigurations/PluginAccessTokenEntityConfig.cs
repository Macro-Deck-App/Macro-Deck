using MacroDeckHost.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MacroDeckHost.Infrastructure.Persistence.EntityConfigurations;

public class PluginAccessTokenEntityConfig : BaseEntityConfig<PluginAccessTokenEntity>
{
	public PluginAccessTokenEntityConfig()
		: base("plugin_access_token", "pat_")
	{
	}

	public override void Configure(EntityTypeBuilder<PluginAccessTokenEntity> builder)
	{
		base.Configure(builder);

		builder.Property(x => x.Name).HasColumnName(ColumnPrefix + "name");
		builder.Property(x => x.TokenHash).HasColumnName(ColumnPrefix + "token_hash");
		builder.Property(x => x.Scopes).HasColumnName(ColumnPrefix + "scopes");
		builder.Property(x => x.ExpiresAt).HasColumnName(ColumnPrefix + "expires_at");
		builder.Property(x => x.LastUsedAt).HasColumnName(ColumnPrefix + "last_used_at");
		builder.Property(x => x.RevokedAt).HasColumnName(ColumnPrefix + "revoked_at");

		builder.HasIndex(x => x.TokenHash).IsUnique();
	}
}
