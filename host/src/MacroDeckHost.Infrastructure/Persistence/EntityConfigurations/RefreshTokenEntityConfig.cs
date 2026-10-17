using MacroDeckHost.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MacroDeckHost.Infrastructure.Persistence.EntityConfigurations;

public class RefreshTokenEntityConfig : BaseEntityConfig<RefreshTokenEntity>
{
	public RefreshTokenEntityConfig()
		: base("refresh_token", "rt_")
	{
	}

	public override void Configure(EntityTypeBuilder<RefreshTokenEntity> builder)
	{
		base.Configure(builder);

		builder.Property(x => x.UserId).HasColumnName(ColumnPrefix + "user_id");
		builder.Property(x => x.TokenHash).HasColumnName(ColumnPrefix + "token_hash");
		builder.Property(x => x.FamilyId)
			.HasColumnName(ColumnPrefix + "family_id")
			.HasDefaultValueSql("''")
			.ValueGeneratedNever();
		builder.Property(x => x.DeviceId).HasColumnName(ColumnPrefix + "device_id");
		builder.Property(x => x.Scope).HasColumnName(ColumnPrefix + "scope");
		builder.Property(x => x.ExpiresAt).HasColumnName(ColumnPrefix + "expires_at");
		builder.Property(x => x.RevokedAt).HasColumnName(ColumnPrefix + "revoked_at");
		builder.Property(x => x.ReplacedById).HasColumnName(ColumnPrefix + "replaced_by_id");
		builder.Property(x => x.RotatedByGrace)
			.HasColumnName(ColumnPrefix + "rotated_by_grace")
			.HasDefaultValue(false)
			.ValueGeneratedNever();

		builder.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("ux_refresh_token_hash");
		builder.HasIndex(x => x.UserId).HasDatabaseName("ix_refresh_token_user");
		builder.HasIndex(x => x.DeviceId).HasDatabaseName("ix_refresh_token_device");
		builder.HasIndex(x => x.FamilyId).HasDatabaseName("ix_refresh_token_family");
	}
}
