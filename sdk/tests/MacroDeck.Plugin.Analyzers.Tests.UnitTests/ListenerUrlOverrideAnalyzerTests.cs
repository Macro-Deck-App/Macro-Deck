using MacroDeck.Plugin.Analyzers.Rules;
using MacroDeck.Plugin.Analyzers.Tests.UnitTests.Support;

namespace MacroDeck.Plugin.Analyzers.Tests.UnitTests;

/// <summary>
/// MDP4002 fires on three independent shapes. UseUrls gets the usual positive/near-miss pair; the other
/// two (a Configuration["urls"] write, and ASPNETCORE_URLS in launchSettings.json) each get a proof that
/// they fire at all, since each is a structurally different trigger the UseUrls pair says nothing about -
/// the launchSettings.json case in particular exercises a wholly different analyzer entry point
/// (RegisterAdditionalFileAction) that nothing else here touches.
/// </summary>
[TestFixture]
public class ListenerUrlOverrideAnalyzerTests
{
	[Test]
	public async Task Fires_when_UseUrls_is_called_on_the_web_host_builder()
	{
		const string source = """
							  using Microsoft.AspNetCore.Hosting;

							  internal static class Startup
							  {
							  	public static void Configure(IWebHostBuilder builder)
							  	{
							  		builder.UseUrls("http://127.0.0.1:9999");
							  	}
							  }
							  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, new ListenerUrlOverrideAnalyzer());

		Assert.That(diagnostics, Has.One.Items);
		Assert.That(diagnostics[0].Id, Is.EqualTo("MDP4002"));
		Assert.That(AnalyzerTestHarness.GetSquiggleText(diagnostics[0], source),
			Is.EqualTo("builder.UseUrls(\"http://127.0.0.1:9999\")"));
	}

	[Test]
	public async Task Does_not_fire_on_an_unrelated_web_host_builder_call()
	{
		const string source = """
							  using Microsoft.AspNetCore.Hosting;

							  internal static class Startup
							  {
							  	public static void Configure(IWebHostBuilder builder)
							  	{
							  		builder.UseEnvironment("Development");
							  	}
							  }
							  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, new ListenerUrlOverrideAnalyzer());

		Assert.That(diagnostics, Is.Empty);
	}

	[Test]
	public async Task Fires_when_the_urls_configuration_key_is_written()
	{
		const string source = """
							  using Microsoft.Extensions.Configuration;

							  internal static class Startup
							  {
							  	public static void Configure(IConfiguration configuration)
							  	{
							  		configuration["urls"] = "http://127.0.0.1:9999";
							  	}
							  }
							  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, new ListenerUrlOverrideAnalyzer());

		Assert.That(diagnostics, Has.One.Items);
		Assert.That(diagnostics[0].Id, Is.EqualTo("MDP4002"));
	}

	[Test]
	public async Task Fires_when_launchSettings_json_sets_ASPNETCORE_URLS()
	{
		const string launchSettings = """
									  {
									    "profiles": {
									      "http": {
									        "commandName": "Project",
									        "environmentVariables": {
									          "ASPNETCORE_URLS": "http://127.0.0.1:9999"
									        }
									      }
									    }
									  }
									  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsFromAdditionalFileAsync(
			"Properties/launchSettings.json",
			launchSettings,
			new ListenerUrlOverrideAnalyzer());

		Assert.That(diagnostics, Has.One.Items);
		Assert.That(diagnostics[0].Id, Is.EqualTo("MDP4002"));
	}

	[Test]
	public async Task Does_not_fire_on_an_unrelated_additional_file()
	{
		const string appSettings = """{ "Logging": { "LogLevel": { "Default": "Information" } } }""";

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsFromAdditionalFileAsync("appsettings.json",
			appSettings,
			new ListenerUrlOverrideAnalyzer());

		Assert.That(diagnostics, Is.Empty);
	}
}
