using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Variables.Colors;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal static class TestColors
{
	public static IColorReferenceResolver None => new ColorReferenceResolver(new VariableRegistry());

	public static PluginColorWatches Watches
		=> new(None,
			new FakeFolderCache(),
			new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None),
			new ColorChangeSignal(),
			Serilog.Core.Logger.None);
}
