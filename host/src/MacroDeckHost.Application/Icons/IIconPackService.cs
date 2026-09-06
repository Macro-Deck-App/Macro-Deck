using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Icons;

public interface IIconPackService
{
	Task<Result<IconPackEntity, IconPackError>> Create(string name,
		string? description,
		string? author,
		string? version);

	Task<Result<IconPackEntity, IconPackError>> Update(Guid id,
		string name,
		string? description,
		string? author,
		string? version);

	Task<Result<IconPackError>> Delete(Guid id);
}
