using MacroDeck.Localization;

namespace MacroDeckHost.Application.Ui.Transport.Messages;

public class TransportError
{
	public string Code { get; set; } = string.Empty;

	public LocalizedText Message { get; set; }
}
