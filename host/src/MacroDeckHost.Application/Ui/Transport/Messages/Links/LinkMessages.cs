namespace MacroDeckHost.Application.Ui.Transport.Messages.Links;

public sealed record UiOpenUrlEvent
{
	public required string Url { get; init; }
}
