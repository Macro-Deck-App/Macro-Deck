using MacroDeck.Localization;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Portable;

public sealed record PortableIntegrationInfo(
	string Id,
	LocalizedText Name,
	string Version,
	bool RequiresConfiguration,
	PortableIntegrationAvailability Availability);

public sealed record PortableArchiveInfo(
	PortableArchiveKind Kind,
	string Name,
	string AppVersion,
	DateTime CreatedAt,
	bool Encrypted,
	bool IncludesSecrets,
	int FolderCount,
	int WidgetCount,
	int IconCount,
	int ScriptCount,
	int SecretCount,
	int VariableCount,
	IReadOnlyList<PortableIntegrationInfo> Integrations);

public interface IPortableArchiveInspector
{
	Task<Result<PortableArchiveInfo, PortabilityError>> Inspect(byte[] archiveBytes,
		CancellationToken cancellationToken);
}
