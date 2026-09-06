using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Secrets;

public interface ISecretService
{
	Task<Guid> Create(string value, SecretKind kind);

	Task<bool> Replace(Guid id, string value);

	Task<bool> Delete(Guid id);

	Task<Guid?> Clone(Guid id);

	Task<string?> Reveal(Guid id);

	Task<string?> Resolve(Guid id);

	Task<SecretMaterial?> ExportForArchive(Guid id);
}
