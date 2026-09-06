using MacroDeckHost.Application.Rendering;
using MacroDeck.Localization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Widgets;

public class WidgetStateUpdatedEvent
{
	public string WidgetId { get; set; } = string.Empty;

	public string StateId { get; set; } = string.Empty;

	public LocalizedText StateLabel { get; set; }

	/// <summary>
	/// The live state set. Filled on the SubscribeWidgetState reply and whenever the set changed;
	/// omitted (null) on a push that only changes which state is active.
	/// </summary>
	public List<WidgetStateOption>? States { get; set; }
}
