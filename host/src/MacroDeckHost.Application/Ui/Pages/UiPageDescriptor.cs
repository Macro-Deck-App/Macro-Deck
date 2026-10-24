using MacroDeck.Localization;

namespace MacroDeckHost.Application.Ui.Pages;

public sealed record UiPageDescriptor
{
	public required string Id { get; init; }

	public required LocalizedText Title { get; init; }

	public string? IconName { get; init; }
}
