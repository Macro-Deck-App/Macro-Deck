using MacroDeckHost.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MacroDeckHost.Infrastructure.Persistence.EntityConfigurations;

public class McpAccessTokenEntityConfig : BaseEntityConfig<McpAccessTokenEntity>
{
	// The timestamp interceptor stamps CreatedAt with local time; the token columns are stored and read as UTC.
	private static readonly ValueConverter<DateTime, DateTime> _utc = new(value => ToUtc(value),
		value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

	private static readonly ValueConverter<DateTime?, DateTime?> _nullableUtc = new(value
			=> value.HasValue ? ToUtc(value.Value) : null,
		value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null);

	public McpAccessTokenEntityConfig()
		: base("mcp_access_token", "mat_")
	{
	}

	public override void Configure(EntityTypeBuilder<McpAccessTokenEntity> builder)
	{
		base.Configure(builder);

		builder.Property(x => x.Name).HasColumnName(ColumnPrefix + "name");
		builder.Property(x => x.TokenHash).HasColumnName(ColumnPrefix + "token_hash");
		builder.Property(x => x.LastUsedAt).HasColumnName(ColumnPrefix + "last_used_at").HasConversion(_nullableUtc);
		builder.Property(x => x.RevokedAt).HasColumnName(ColumnPrefix + "revoked_at").HasConversion(_nullableUtc);
		builder.Property(x => x.CreatedAt).HasConversion(_utc);

		builder.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("ux_mcp_access_token_hash");
	}

	private static DateTime ToUtc(DateTime value)
	{
		return value.Kind == DateTimeKind.Local
			? value.ToUniversalTime()
			: DateTime.SpecifyKind(value, DateTimeKind.Utc);
	}
}
