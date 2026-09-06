using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Application.Widgets;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

// Exists() answers from a fixed set of ids handed to the constructor, rather than reading any real
// widget storage.
internal sealed class FakeWidgetAppearanceService : IWidgetAppearanceService
{
	private readonly HashSet<string> _existingIds;

	public FakeWidgetAppearanceService(params string[] existingIds)
		=> _existingIds = new HashSet<string>(existingIds, StringComparer.Ordinal);

	public IReadOnlyList<WidgetTargetInfo> GetWidgets() => [];

	public bool Exists(string widgetId) => _existingIds.Contains(widgetId);

	public Task<bool> ApplyAsync(WidgetAppearanceRequest request, CancellationToken cancellationToken = default)
		=> Task.FromResult(false);
}
