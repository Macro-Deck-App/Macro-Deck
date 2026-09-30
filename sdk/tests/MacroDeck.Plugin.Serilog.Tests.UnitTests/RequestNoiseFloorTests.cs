using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Serilog.Tests.UnitTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace MacroDeck.Plugin.Serilog.Tests.UnitTests;

[TestFixture]
public class RequestNoiseFloorTests
{
	private const string RequestCategory = "Microsoft.AspNetCore.Hosting.Diagnostics";
	private const string HttpClientCategory = "System.Net.Http.HttpClient";

	private static readonly string[] RequestPipelineCategories =
	[
		"Microsoft.AspNetCore.Hosting.Diagnostics",
		"Microsoft.AspNetCore.Routing.EndpointMiddleware",
		"Microsoft.AspNetCore.Http.Result",
		"Microsoft.AspNetCore.Mvc",
		"Microsoft.AspNetCore.Cors.Infrastructure.CorsService",
		"Microsoft.AspNetCore.StaticFiles",
		HttpClientCategory
	];

	[TestCase("Microsoft.AspNetCore.Hosting.Diagnostics")]
	[TestCase("Microsoft.AspNetCore.Routing.EndpointMiddleware")]
	[TestCase("Microsoft.AspNetCore.Http.Result.OkObjectResult")]
	[TestCase("Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker")]
	[TestCase("Microsoft.AspNetCore.StaticFiles.StaticFileMiddleware")]
	[TestCase("System.Net.Http.HttpClient.Default.ClientHandler")]
	public void Information_from_the_request_pipeline_is_dropped_and_a_warning_is_kept(string category)
	{
		using var contentRoot = new PluginContentRoot();
		var provider = new CapturingLoggerProvider();
		using var plugin = BuildPlugin(contentRoot, provider, configure: null);

		var logger = plugin.Services.GetRequiredService<ILoggerFactory>().CreateLogger(category);
		logger.LogInformation("routine request line");
		logger.LogWarning("something worth reading");

		Assert.That(provider.Entries.Select(entry => (entry.Level, entry.Message)),
			Is.EqualTo(new[] { (LogLevel.Warning, "something worth reading") }));
	}

	[Test]
	public void Startup_kestrel_and_the_authors_own_information_are_still_logged()
	{
		using var contentRoot = new PluginContentRoot();
		var provider = new CapturingLoggerProvider();
		using var plugin = BuildPlugin(contentRoot, provider, configure: null);

		var factory = plugin.Services.GetRequiredService<ILoggerFactory>();
		factory.CreateLogger("Microsoft.Hosting.Lifetime").LogInformation("Application started");
		factory.CreateLogger("Microsoft.AspNetCore.Server.Kestrel").LogInformation("Connection id bad request");
		factory.CreateLogger("Acme.Plugin.Thing").LogInformation("connected to the bridge");

		Assert.That(provider.Messages,
			Is.EqualTo(new[] { "Application started", "Connection id bad request", "connected to the bridge" }));
	}

	[Test]
	public void A_more_specific_override_in_the_configure_callback_brings_the_lines_back()
	{
		using var contentRoot = new PluginContentRoot();
		var provider = new CapturingLoggerProvider();
		using var plugin = BuildPlugin(contentRoot, provider,
			cfg => cfg.MinimumLevel.Override(RequestCategory, LogEventLevel.Information));

		plugin.Services.GetRequiredService<ILoggerFactory>()
			.CreateLogger(RequestCategory).LogInformation("Request finished");

		Assert.That(provider.Messages, Is.EqualTo(new[] { "Request finished" }));
	}

	[Test]
	public void A_lower_global_minimum_alone_does_not_bring_the_lines_back()
	{
		using var contentRoot = new PluginContentRoot();
		var provider = new CapturingLoggerProvider();
		using var plugin = BuildPlugin(contentRoot, provider, cfg => cfg.MinimumLevel.Debug());

		plugin.Services.GetRequiredService<ILoggerFactory>()
			.CreateLogger(RequestCategory).LogInformation("Request finished");

		Assert.That(provider.Entries, Is.Empty);
	}

	[Test]
	public async Task A_successful_health_poll_over_the_real_pipeline_logs_no_request_lines()
	{
		var entries = await PollHealthTwiceAsync(configure: null);

		Assert.That(NoiseFrom(entries), Is.Empty);
	}

	[Test]
	public async Task The_same_health_poll_does_log_request_lines_when_the_floors_are_lifted()
	{
		var entries = await PollHealthTwiceAsync(cfg => cfg
			.MinimumLevel.Override(RequestCategory, LogEventLevel.Information)
			.MinimumLevel.Override(HttpClientCategory, LogEventLevel.Information));

		var noise = NoiseFrom(entries);
		Assert.That(noise, Has.Some.Matches<CapturedLogEntry>(entry =>
			entry.Category.StartsWith(RequestCategory, StringComparison.Ordinal)
			&& entry.Message.Contains("Request finished", StringComparison.Ordinal)
			&& entry.Message.Contains("/_macrodeck/health", StringComparison.Ordinal)));
		Assert.That(noise, Has.Some.Matches<CapturedLogEntry>(entry =>
			entry.Category.StartsWith(HttpClientCategory, StringComparison.Ordinal)
			&& entry.Message.Contains("/_macrodeck/health", StringComparison.Ordinal)));
	}

	private static List<CapturedLogEntry> NoiseFrom(IReadOnlyList<CapturedLogEntry> entries) =>
		entries.Where(entry => entry.Level == LogLevel.Information
			&& RequestPipelineCategories.Any(category => entry.Category.StartsWith(category, StringComparison.Ordinal)))
			.ToList();

	// Two requests on one keep-alive connection: the first request's closing lines are logged before
	// the server reads the second, so both are in the capture once the second response is back.
	private static async Task<IReadOnlyList<CapturedLogEntry>> PollHealthTwiceAsync(
		Action<LoggerConfiguration>? configure)
	{
		using var contentRoot = new PluginContentRoot();
		var provider = new CapturingLoggerProvider();
		await using var plugin = BuildPlugin(contentRoot, provider, configure);

		await plugin.StartAsync();
		try
		{
			var address = plugin.WebApplication.Services.GetRequiredService<IServer>()
				.Features.Get<IServerAddressesFeature>()!.Addresses.First();
			var client = plugin.Services.GetRequiredService<IHttpClientFactory>().CreateClient();

			for (var poll = 0; poll < 2; poll++)
			{
				using var response = await client.GetAsync(new Uri($"{address}/_macrodeck/health"));
				Assert.That(response.IsSuccessStatusCode, Is.True);
			}
		}
		finally
		{
			await plugin.StopAsync();
		}

		return provider.Entries;
	}

	// The host URL points at a closed port with pairing off, so a started plugin never reaches a real host.
	private static PluginApplication BuildPlugin(
		PluginContentRoot contentRoot,
		CapturingLoggerProvider provider,
		Action<LoggerConfiguration>? configure)
	{
		var builder = MacroDeckPlugin.CreatePlugin(["--contentRoot", contentRoot.RootPath])
			.UseMacroDeckLogging(configure);
		builder.Configuration["MacroDeck:Plugin:HostUrl"] = "http://127.0.0.1:1";
		builder.Configuration["MacroDeck:Plugin:PairingEnabled"] = "false";
		builder.Configuration["MacroDeck:Plugin:StateDirectory"] = Path.Combine(contentRoot.RootPath, "state");
		builder.Services.AddSingleton<ILoggerProvider>(provider);
		return builder.Build();
	}
}
