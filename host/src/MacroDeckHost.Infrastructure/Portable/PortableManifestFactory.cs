using MacroDeckHost.Application.Portable;
using MacroDeckHost.Application.Services;

namespace MacroDeckHost.Infrastructure.Portable;

internal static class PortableManifestFactory
{
	public static PortableArchiveManifest Create(PortableArchiveKind kind,
		PortableExportOptions options,
		PortableContent content,
		PortableAssetBundle assets,
		string name,
		int widgetCount)
		=> new()
		{
			FormatVersion = PortableArchiveManifest.CurrentFormatVersion,
			Kind = kind,
			AppVersion = HostVersion.Current,
			CreatedAt = DateTime.UtcNow,
			IncludesSecrets = options.IncludeSecrets,
			Contents = new PortableArchiveContents
			{
				Name = name,
				FolderCount = content.Folders?.Count ?? content.Profile?.Folders.Count ?? 0,
				WidgetCount = widgetCount,
				IconCount = content.Icons.Count,
				ScriptCount = content.Scripts.Count,
				SecretCount = content.Secrets.Count,
				VariableCount = content.Variables.Count,
				Integrations = assets.Integrations.ToList()
			}
		};
}
