using MacroDeckHost.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MacroDeckHost.Infrastructure.Persistence.EntityConfigurations;

public class SecretEntityConfig : BaseEntityConfig<SecretEntity>
{
	public SecretEntityConfig()
		: base("secret", "s_")
	{
	}

	public override void Configure(EntityTypeBuilder<SecretEntity> builder)
	{
		base.Configure(builder);

		builder.Property(x => x.Kind).HasColumnName(ColumnPrefix + "kind");
		builder.Property(x => x.EncryptedValue).HasColumnName(ColumnPrefix + "encrypted_value");
		builder.Property(x => x.UpdatedAt).HasColumnName(ColumnPrefix + "updated_at");
	}
}
