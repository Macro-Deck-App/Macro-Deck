using MacroDeckHost.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MacroDeckHost.Infrastructure.Persistence.EntityConfigurations;

public class UserEntityConfig : BaseEntityConfig<UserEntity>
{
	public UserEntityConfig()
		: base("app_user", "u_")
	{
	}

	public override void Configure(EntityTypeBuilder<UserEntity> builder)
	{
		base.Configure(builder);

		builder.Property(x => x.Username).HasColumnName(ColumnPrefix + "username");
		builder.Property(x => x.PasswordHash).HasColumnName(ColumnPrefix + "password_hash");
		builder.Property(x => x.UpdatedAt).HasColumnName(ColumnPrefix + "updated_at");
	}
}
