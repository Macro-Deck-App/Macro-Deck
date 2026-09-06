using MacroDeck.Localization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Actions;

public class ActionParameterOptionDto
{
	public string Value { get; set; } = string.Empty;

	public LocalizedText Label { get; set; }

	public Dictionary<string, string>? Metadata { get; set; }
}
