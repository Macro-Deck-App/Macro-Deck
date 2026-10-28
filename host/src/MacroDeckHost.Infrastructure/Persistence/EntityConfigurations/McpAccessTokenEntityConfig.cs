using MacroDeckHost.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MacroDeckHost.Infrastructure.Persistence.EntityConfigurations;

public class McpAccessTokenEntityConfig : BaseEntityConfig<McpAccessTokenEntity>
{
	public McpAccessTokenEntityConfig()
		: base("mcp_access_token", "mat_")
	{
	}

	public override void Configure(EntityTypeBuilder<McpAccessTokenEntity> builder)
	{
		base.Configure(builder);

		builder.Property(x => x.Name).HasColumnName(ColumnPrefix + "name");
		builder.Property(x => x.TokenHash).HasColumnName(ColumnPrefix + "token_hash");
		builder.Property(x => x.LastUsedAt).HasColumnName(ColumnPrefix + "last_used_at");
		builder.Property(x => x.RevokedAt).HasColumnName(ColumnPrefix + "revoked_at");

		builder.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("ux_mcp_access_token_hash");
	}
}
