using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MacroDeck.Localization;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Localization;
using MacroDeckHost.Tests.UnitTests.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Host;

[NonParallelizable]
public class OAuthCallbackEndpointTests
{
	private const string InjectedMarkup = "<img src=x onerror=alert(document.domain)>";

	private IHost _host = null!;
	private HttpClient _client = null!;
	private string _dataDir = null!;
	private string? _previousDataDir;

	[SetUp]
	public async Task StartHost()
	{
		_dataDir = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		_previousDataDir = Environment.GetEnvironmentVariable("MACRODECK_DATA_DIR");
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _dataDir);
		DatabaseMigrationHelper.MigrateDatabase(new MacroDeckPaths());

		_host = await new HostBuilder()
			.ConfigureWebHost(builder =>
			{
				builder.UseTestServer();
				builder.UseStartup<Startup>();
				builder.ConfigureTestServices(services =>
				{
					services.RemoveAll<IHostedService>();
					services.AddSingleton(Log.Logger);
					services.AddSingleton<ILogLevelState>(new LogLevelState(LogEntryLevel.Information));
					services.AddSingleton<IStartupFilter, FakeConnectionStartupFilter>();
					services.RemoveAll<StartupReadiness>();
					var readiness = new StartupReadiness();
					readiness.MarkCachesReady();
					readiness.MarkVariablesReady();
					services.AddSingleton(readiness);
				});
			})
			.StartAsync();
		_client = _host.GetTestClient();
	}

	[TearDown]
	public async Task StopHost()
	{
		_client.Dispose();
		await _host.StopAsync();
		_host.Dispose();
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _previousDataDir);
		if (Directory.Exists(_dataDir))
		{
			Directory.Delete(_dataDir, true);
		}
	}

	[Test]
	public async Task Markup_in_the_error_of_a_matched_callback_is_never_rendered()
	{
		var state = _host.Services.GetRequiredService<IOAuthCallbackCoordinator>().Register(Guid.NewGuid()).State;

		var (_, body) = await Callback($"state={state}&error={Uri.EscapeDataString(InjectedMarkup)}");

		Assert.That(body, Does.Not.Contain("<img"));
	}

	[TestCase(true)]
	[TestCase(false)]
	public async Task The_page_may_only_run_its_own_inline_script_and_style(bool matched)
	{
		var state = matched
			? _host.Services.GetRequiredService<IOAuthCallbackCoordinator>().Register(Guid.NewGuid()).State
			: "unknown";

		var (response, body) = await Callback($"state={state}&code=abc");
		var policy = string.Join(";", response.Headers.GetValues("Content-Security-Policy"));

		Assert.Multiple(() =>
		{
			Assert.That(policy, Does.Contain("default-src 'none'"));
			Assert.That(policy, Does.Not.Contain("unsafe-inline"));
			foreach (var element in new[] { "script", "style" })
			{
				var blocks = Regex.Matches(body, $"<{element}>(.*?)</{element}>", RegexOptions.Singleline);
				Assert.That(blocks, Is.Not.Empty, element);
				foreach (Match block in blocks)
				{
					Assert.That(policy, Does.Contain($"'sha256-{Sha256(block.Groups[1].Value)}'"), element);
				}
			}

			Assert.That(response.Headers.GetValues("X-Content-Type-Options"), Is.EqualTo(new[] { "nosniff" }));
			Assert.That(response.Headers.GetValues("Referrer-Policy"), Is.EqualTo(new[] { "no-referrer" }));
			Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
		});
	}

	[Test]
	public async Task The_page_speaks_the_language_chosen_for_Macro_Deck()
	{
		await using (var scope = _host.Services.CreateAsyncScope())
		{
			await scope.ServiceProvider.GetRequiredService<IAppPreferenceService>().SetLocalization("de");
		}

		var title = _host.Services.GetRequiredService<ILocalizationResolver>()
			.Resolve(AppStrings.ConfigFlow.OAuthCallback.CompleteTitle(), "de");
		var state = _host.Services.GetRequiredService<IOAuthCallbackCoordinator>().Register(Guid.NewGuid()).State;

		var (_, body) = await Callback($"state={state}&code=abc");

		Assert.Multiple(() =>
		{
			Assert.That(body, Does.Contain("<html lang=\"de\""));
			Assert.That(body, Does.Contain($"<h1>{title}</h1>"));
			Assert.That(body, Does.Not.Contain("Authorization complete"));
		});
	}

	private async Task<(HttpResponseMessage Response, string Body)> Callback(string query)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/integrations/oauth/callback?{query}");
		request.Headers.Add(FakeConnectionStartupFilter.ShapeHeader, nameof(FakeConnectionShape.LoopbackOnPublicPort));
		var response = await _client.SendAsync(request);
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		return (response, await response.Content.ReadAsStringAsync());
	}

	private static string Sha256(string text)
	{
		return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
	}
}
