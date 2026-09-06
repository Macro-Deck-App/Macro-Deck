using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Infrastructure.Integrations;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal static class TestWidgetTypeProviders
{
	/// <summary>A fresh, empty widget type registry - so a test about something else gets the built-in
	/// types and nothing more.</summary>
	public static IWidgetTypeRegistry Registry() => new WidgetTypeRegistry(new RecordingMediator());

	/// <summary>A widget-type-provider host wired to a fresh registry, for tests about something
	/// else.</summary>
	public static WidgetTypeProviderHost Host() =>
		new(Registry(), TimeProvider.System, Serilog.Core.Logger.None);
}
