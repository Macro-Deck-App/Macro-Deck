using MacroDeck.Localization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Actions;

/// <summary>What a provider suggests a state should look like before the user styles it (issue #612).</summary>
public class ActionProviderStateAppearanceDto
{
	public string? Label { get; set; }

	public string? BackgroundColor { get; set; }

	public string? LabelColor { get; set; }

	public string? IconId { get; set; }
}

public class ActionProviderStateDto
{
	public string Id { get; set; } = string.Empty;

	public LocalizedText Label { get; set; }

	public ActionProviderStateAppearanceDto? DefaultAppearance { get; set; }
}

public class GetActionProviderStatesResponse
{
	public List<ActionProviderStateDto> States { get; set; } = [];

	public TransportError? Error { get; set; }
}
