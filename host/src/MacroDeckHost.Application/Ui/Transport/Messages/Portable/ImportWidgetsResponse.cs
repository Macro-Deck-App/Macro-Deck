using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Portable;

public sealed class ImportWidgetsResponse
{
	public bool Success { get; set; }

	public TransportError? Error { get; set; }

	public List<Widget> Widgets { get; set; } = [];
}
