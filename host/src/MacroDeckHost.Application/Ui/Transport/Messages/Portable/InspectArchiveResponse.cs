using MacroDeck.Localization;
using MacroDeckHost.Application.Portable;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Portable;

public sealed class ArchiveIntegration
{
	public string Id { get; set; } = string.Empty;

	public LocalizedText Name { get; set; }

	public string Version { get; set; } = string.Empty;

	public bool RequiresConfiguration { get; set; }

	public string Availability { get; set; } = string.Empty;
}

public sealed class ArchiveSummary
{
	public string Kind { get; set; } = string.Empty;

	public string Name { get; set; } = string.Empty;

	public string AppVersion { get; set; } = string.Empty;

	public DateTime CreatedAt { get; set; }

	public bool Encrypted { get; set; }

	public bool IncludesSecrets { get; set; }

	public int FolderCount { get; set; }

	public int WidgetCount { get; set; }

	public int IconCount { get; set; }

	public int ScriptCount { get; set; }

	public int SecretCount { get; set; }

	public int VariableCount { get; set; }

	public List<ArchiveIntegration> Integrations { get; set; } = [];
}

public sealed class InspectArchiveResponse
{
	public bool Success { get; set; }

	public TransportError? Error { get; set; }

	public ArchiveSummary? Archive { get; set; }

	public static InspectArchiveResponse From(PortableArchiveInfo info)
		=> new()
		{
			Success = true,
			Archive = new ArchiveSummary
			{
				Kind = info.Kind.ToString(),
				Name = info.Name,
				AppVersion = info.AppVersion,
				CreatedAt = info.CreatedAt,
				Encrypted = info.Encrypted,
				IncludesSecrets = info.IncludesSecrets,
				FolderCount = info.FolderCount,
				WidgetCount = info.WidgetCount,
				IconCount = info.IconCount,
				ScriptCount = info.ScriptCount,
				SecretCount = info.SecretCount,
				VariableCount = info.VariableCount,
				Integrations = info.Integrations
					.Select(integration => new ArchiveIntegration
					{
						Id = integration.Id,
						Name = integration.Name,
						Version = integration.Version,
						RequiresConfiguration = integration.RequiresConfiguration,
						Availability = integration.Availability.ToString()
					})
					.ToList()
			}
		};
}
