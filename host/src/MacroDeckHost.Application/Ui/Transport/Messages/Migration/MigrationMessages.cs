using MacroDeck.Localization;
using MacroDeckHost.Application.Migration;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Migration;

public sealed class MigrationSourceDto
{
	public string Id { get; set; } = string.Empty;

	public string Name { get; set; } = string.Empty;

	/// <summary>Where this application's data was found on this machine, when it could be found at all.</summary>
	public string? DefaultPath { get; set; }
}

public sealed class MigrationSourcesResponse
{
	public List<MigrationSourceDto> Sources { get; set; } = [];
}

public sealed class MigrationRequestBody
{
	public string SourceId { get; set; } = string.Empty;

	public string Path { get; set; } = string.Empty;

	/// <summary>Supplied only after the host reported that it has no usable key of its own.</summary>
	public string? DecryptionKey { get; set; }

	public bool SkipDecryption { get; set; }
}

public sealed class MigrationWarningDto
{
	public string Kind { get; set; } = string.Empty;

	public string Subject { get; set; } = string.Empty;

	/// <summary>
	/// Sent as a reference rather than resolved text, so the client that renders it picks the language -
	/// the same contract every other user-facing string on this transport follows.
	/// </summary>
	public LocalizedText Detail { get; set; }
}

public sealed class MigrationUnsupportedActionDto
{
	public string SourcePlugin { get; set; } = string.Empty;

	public string SourceAction { get; set; } = string.Empty;

	public int Occurrences { get; set; }
}

public sealed class MigrationIntegrationDto
{
	public string Id { get; set; } = string.Empty;

	public string Title { get; set; } = string.Empty;

	public bool CarriesCredentials { get; set; }
}

public sealed class MigrationSummary
{
	public string SourceId { get; set; } = string.Empty;

	public string SourceName { get; set; } = string.Empty;

	public int ProfileCount { get; set; }

	public int FolderCount { get; set; }

	public int WidgetCount { get; set; }

	public int IconCount { get; set; }

	public int VariableCount { get; set; }

	public int MigratedActionCount { get; set; }

	public int UnsupportedActionCount { get; set; }

	/// <summary>
	/// One of the <c>MigrationCredentialStatus</c> names. Anything other than <c>NotPresent</c> or
	/// <c>Decrypted</c> is what makes the client offer the choice between skipping encrypted data and
	/// entering the key by hand.
	/// </summary>
	public string CredentialStatus { get; set; } = string.Empty;

	public List<MigrationIntegrationDto> Integrations { get; set; } = [];

	public List<MigrationUnsupportedActionDto> UnsupportedActions { get; set; } = [];

	public List<MigrationWarningDto> Warnings { get; set; } = [];

	public static MigrationSummary From(MigrationPlan plan)
		=> new()
		{
			SourceId = plan.SourceId,
			SourceName = plan.SourceName,
			ProfileCount = plan.Profiles.Count,
			FolderCount = plan.FolderCount,
			WidgetCount = plan.WidgetCount,
			IconCount = plan.Icons.Count,
			VariableCount = plan.Variables.Count,
			MigratedActionCount = plan.MigratedActionCount,
			UnsupportedActionCount = plan.UnsupportedActions.Sum(action => action.Occurrences),
			CredentialStatus = plan.CredentialStatus.ToString(),
			Integrations = plan.IntegrationConfigs
				.Select(config => new MigrationIntegrationDto
				{
					Id = config.IntegrationId,
					Title = config.Title,
					CarriesCredentials = config.Secrets.Count > 0
				})
				.ToList(),
			UnsupportedActions = plan.UnsupportedActions
				.Select(action => new MigrationUnsupportedActionDto
				{
					SourcePlugin = action.SourcePlugin,
					SourceAction = action.SourceAction,
					Occurrences = action.Occurrences
				})
				.ToList(),
			Warnings = plan.Warnings
				.Select(warning => new MigrationWarningDto
				{
					Kind = warning.Kind.ToString(),
					Subject = warning.Subject,
					Detail = warning.Detail
				})
				.ToList()
		};
}

public sealed class MigrationPreviewResponse
{
	public bool Success { get; set; }

	public TransportError? Error { get; set; }

	public MigrationSummary? Summary { get; set; }
}

public sealed class MigrationImportResponse
{
	public bool Success { get; set; }

	public TransportError? Error { get; set; }

	public MigrationSummary? Summary { get; set; }

	public List<Guid> ProfileIds { get; set; } = [];
}
