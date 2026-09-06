using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Infrastructure.Integrations;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal static class TestLayoutProviders
{
	/// <summary>A layout-provider host wired to a fresh registry, for tests about something else.</summary>
	public static LayoutProviderHost Host() =>
		new(new LayoutRegistry(new RecordingMediator()), TimeProvider.System, Serilog.Core.Logger.None);
}
