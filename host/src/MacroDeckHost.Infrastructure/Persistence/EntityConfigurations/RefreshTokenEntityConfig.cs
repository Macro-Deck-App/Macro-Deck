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
		builder.Property(x => x.DeviceId).HasColumnName(ColumnPrefix + "device_id");
		builder.Property(x => x.Scope).HasColumnName(ColumnPrefix + "scope");
		builder.Property(x => x.ExpiresAt).HasColumnName(ColumnPrefix + "expires_at");
		builder.Property(x => x.RevokedAt).HasColumnName(ColumnPrefix + "revoked_at");
		builder.Property(x => x.ReplacedById).HasColumnName(ColumnPrefix + "replaced_by_id");
	}
}
