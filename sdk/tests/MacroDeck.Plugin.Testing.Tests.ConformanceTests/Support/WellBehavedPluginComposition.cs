using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Serilog;
using MacroDeck.Plugin.Testing.Tests.WellBehavedPlugin;

namespace MacroDeck.Plugin.Testing.Tests.ConformanceTests.Support;

/// <summary>
/// How this suite hosts the well-behaved fixture as an in-process subject. Its own <c>Program.cs</c> is
/// the plain top-level-statement form every plugin uses, which leaves nothing for test code to call -
/// so the composition is restated here, on the test side, rather than shaping the fixture around this
/// suite's needs.
///
/// <para>
/// Kept to one place so the executable and artifact subjects (which run the real <c>Program.cs</c>)
/// and this one cannot drift apart unnoticed: if the fixture registers something else, the
/// in-process report diverges from the other two and <c>ReusabilityTests</c> is what notices.
/// </para>
/// </summary>
internal static class WellBehavedPluginComposition
{
	public static PluginHostBuilder Configure(PluginHostBuilder builder)
		=> builder
			.UseMacroDeckLogging()
			.RegisterIntegration<WellBehavedIntegration>();
}
