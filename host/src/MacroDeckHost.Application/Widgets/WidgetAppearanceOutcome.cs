using MacroDeck.Sdk.Widgets;

namespace MacroDeckHost.Application.Widgets;

public enum WidgetAppearanceOutcome
{
	Changed,
	Unchanged,
	Rejected,
	WriteFailed
}

public interface IWidgetAppearanceOutcomeApi
{
	Task<WidgetAppearanceOutcome> ApplyWithOutcomeAsync(
		WidgetAppearanceRequest request,
		CancellationToken cancellationToken = default);
}
