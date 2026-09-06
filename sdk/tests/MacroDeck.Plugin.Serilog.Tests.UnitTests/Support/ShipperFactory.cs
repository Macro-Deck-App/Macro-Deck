using MacroDeck.Plugin.Hosting;
using Microsoft.Extensions.Options;

namespace MacroDeck.Plugin.Serilog.Tests.UnitTests.Support;

/// <summary>Builds a sink/shipper/transport triple that share the same options, without needing the
/// full <c>PluginHostBuilder</c>/DI container.</summary>
internal static class ShipperFactory
{
	public static (MacroDeckLogSink Sink, MacroDeckLogShipper Shipper, FakeLogTransport Transport) Create(
		MacroDeckLoggingOptions? options = null,
		TimeProvider? timeProvider = null)
	{
		var effectiveOptions = options ?? new MacroDeckLoggingOptions { FlushInterval = TimeSpan.FromMilliseconds(50) };

		var sink = new MacroDeckLogSink(Options.Create(effectiveOptions));
		var transport = new FakeLogTransport();

		var hostOptions = Options.Create(new PluginHostOptions
		{
			StateDirectory = Path.Combine(Path.GetTempPath(), "macrodeck-log-tests", Guid.NewGuid().ToString("N"))
		});

		var metadata = new PluginMetadata { Id = "com.example.test-plugin", Name = "Test Plugin", Version = "1.0.0" };

		var shipper = new MacroDeckLogShipper(sink,
			transport,
			Options.Create(effectiveOptions),
			hostOptions,
			metadata,
			timeProvider ?? TimeProvider.System);

		return (sink, shipper, transport);
	}
}
