using MacroDeck.Localization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Integrations;

public class Integration
{
	public string Id { get; set; } = string.Empty;

	public LocalizedText Name { get; set; }

	public string Version { get; set; } = string.Empty;

	public bool IsInternal { get; set; }

	public bool Enabled { get; set; }

	public bool IsInitialized { get; set; }

	public int ActionCount { get; set; }

	public int VariableCount { get; set; }

	public bool VariablesDependOnConfiguration { get; set; }

	public bool SupportsConfigFlow { get; set; }

	public bool AllowsMultipleConfigurations { get; set; } = true;

	public int ConfiguredEntryCount { get; set; }

	public bool HasIcon { get; set; }

	/// <summary>
	/// Identity of the current icon bytes, or null when there is no icon. The icon URL is keyed by
	/// the integration id alone, so clients append this to make a changed icon a different URL.
	/// </summary>
	public string? IconVersion { get; set; }

	public int IssueCount { get; set; }

	public string? IssueSeverity { get; set; }

	public List<ProvidedCapabilityDto> ProvidedCapabilities { get; set; } = new();
}
