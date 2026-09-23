using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Icons;

public interface IIconPackService
{
	Task<Result<IconPackEntity, IconPackError>> Create(string name,
		string? description,
		string? author,
		string? version,
		IconPackAiAssets aiAssets = IconPackAiAssets.NotDeclared);

	Task<Result<IconPackEntity, IconPackError>> Update(Guid id,
		string name,
		string? description,
		string? author,
		string? version,
		IconPackAiAssets? aiAssets = null);

	Task<Result<IconPackError>> Delete(Guid id);
}
