using MacroDeck.Localization;
using MacroDeck.Sdk.Migration;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Migration;

/// <summary>
/// Everything a source read out of a foreign installation, in the shape the existing importer consumes.
/// Producing one writes nothing, so a preview and a real import run the very same code.
/// </summary>
public sealed class MigrationPlan : IDisposable
{
	public string SourceId { get; set; } = string.Empty;

	public string SourceName { get; set; } = string.Empty;

	/// <summary>The version of the foreign application the data was written by, when it says so.</summary>
	public string? SourceVersion { get; set; }

	public List<PortableContent> Profiles { get; set; } = [];

	public List<MigratedConfiguration> IntegrationConfigs { get; set; } = [];

	public List<MigratedVariable> Variables { get; set; } = [];

	/// <summary>
	/// Images the plan references by <see cref="MigrationIconRequest.PlaceholderId" />. They are only read
	/// off disk when the plan is applied; a preview reports their count and nothing more.
	/// </summary>
	public List<MigrationIconRequest> Icons { get; set; } = [];

	public List<MigrationWarning> Warnings { get; set; } = [];

	public List<UnsupportedAction> UnsupportedActions { get; set; } = [];

	public MigrationCredentialStatus CredentialStatus { get; set; } = MigrationCredentialStatus.NotPresent;

	public int MigratedActionCount { get; set; }

	public int FolderCount => Profiles.Sum(profile => profile.Profile?.Folders.Count ?? 0);

	public int WidgetCount
		=> Profiles.Sum(profile => profile.Profile?.Folders.Sum(folder => folder.Widgets.Count) ?? 0);

	/// <summary>
	/// Whatever the source had to materialise for the plan to be readable - a backup archive unpacked
	/// into a temporary directory, which is what <see cref="Icons" /> name files inside. It lives exactly
	/// as long as the plan does, because those files are opened when the plan is applied, long after the
	/// source finished reading.
	/// </summary>
	public IDisposable? Scope { get; set; }

	public void Dispose()
	{
		Scope?.Dispose();
		Scope = null;
	}
}

/// <summary>One image to pull into the icon catalogue, and the id the widget data references it by until
/// the real icon exists.</summary>
public sealed record MigrationIconRequest(Guid PlaceholderId, string PackName, string FilePath, string Name);

public sealed record MigratedVariable(string Name, VariableType Type, string Value, int? DecimalPlaces);

public sealed record MigrationWarning(MigrationWarningKind Kind, string Subject, LocalizedText Detail);

/// <summary>A foreign action no migrator claimed. It is still placed on the deck, carrying everything the
/// source knew about it, so nothing is silently lost.</summary>
public sealed record UnsupportedAction(string SourcePlugin, string SourceAction, int Occurrences);
