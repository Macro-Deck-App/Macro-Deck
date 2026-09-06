using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Serilog.Tests.UnitTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MacroDeck.Plugin.Serilog.Tests.UnitTests;

/// <summary>
/// A plugin's own log output has to keep reaching its console, not only the host's log viewer (#755): the
/// console is what an IDE's run window shows when the plugin project is the debuggee, and what
/// <c>macrodeck-plugin run</c> forwards from the child process it launched. Asserted through the
/// registered logging providers, because that is the pipeline the console provider a plugin built from
/// <c>WebApplication.CreateBuilder</c> already has is served by.
/// </summary>
[TestFixture]
public class PluginConsoleOutputTests
{
	[Test]
	public void An_authors_log_call_reaches_the_registered_logging_providers()
	{
		using var contentRoot = new PluginContentRoot();
		var provider = new CapturingLoggerProvider();

		var builder = MacroDeckPlugin.CreatePlugin(["--contentRoot", contentRoot.RootPath]).UseMacroDeckLogging();
		builder.Services.AddSingleton<ILoggerProvider>(provider);

		using var plugin = builder.Build();

		plugin.Services.GetRequiredService<ILogger<PluginConsoleOutputTests>>()
			.LogInformation("a line the plugin author logged");

		Assert.That(provider.Messages, Has.One.Contains("a line the plugin author logged"));
	}
}
