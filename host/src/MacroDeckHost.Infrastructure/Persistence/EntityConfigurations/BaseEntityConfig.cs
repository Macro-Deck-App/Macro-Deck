using MacroDeckHost.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MacroDeckHost.Infrastructure.Persistence.EntityConfigurations;

public class BaseEntityConfig<T> : IEntityTypeConfiguration<T>
	where T : BaseEntity
{
	protected string ColumnPrefix { get; }

	private readonly string _tableName;

	public BaseEntityConfig(string tableName, string columnPrefix)
	{
		_tableName = tableName;
		ColumnPrefix = columnPrefix;
	}

	public virtual void Configure(EntityTypeBuilder<T> builder)
	{
		builder.ToTable(_tableName);

		builder.HasKey(x => x.Id);

		builder.Property(x => x.Id).HasColumnName(ColumnPrefix + "id");
		builder.Property(x => x.CreatedAt).HasColumnName(ColumnPrefix + "created_at");
	}
}
