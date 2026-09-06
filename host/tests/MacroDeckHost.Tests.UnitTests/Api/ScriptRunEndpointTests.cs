using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Integrations;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Integrations.Variables;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Api;

// Acceptance scenario S1.1: the only completely untested hop in the delegate chain is the real HTTP
// POST a delegate client sends to the remote's api/scripts/{id}/run. This pins the request body to the
// remote's ScriptInputBinder end-to-end, through the real Startup on a TestServer - exactly the setup
// BackupEndpointsTests already uses for the same reason.
//
// The fixture script's flow observes what the run actually used by writing each supplied input into a
// global variable, read back afterwards through the variables API. It deliberately does NOT reuse the
// input's own name for that global: ScriptInputScope makes a variable named after one of the script's own
// declared inputs read-only for the duration of that script's run (see ScriptInputReadOnlyTests), so a
// same-named global would make every write in this fixture fail instead of observing anything.
[NonParallelizable]
public class ScriptRunEndpointTests
{
	private const string LoopbackHeader = "X-Test-Loopback";

	private static readonly string[] _expectedAppliedInputs = ["scene", "volume", "muted"];

	private IHost _host = null!;
	private HttpClient _client = null!;
	private string _dataDir = null!;
	private string? _previousDataDir;

	[OneTimeSetUp]
	public async Task OneTimeSetUp()
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
					services.AddSingleton<IStartupFilter, LoopbackStartupFilter>();
					services.RemoveAll<StartupReadiness>();
					services.AddSingleton(CompletedStartupReadiness());
				});
			})
			.StartAsync();

		_client = _host.GetTestClient();

		var setup = await Send(HttpMethod.Post,
			"/api/auth/setup",
			new { username = "admin", password = "password123" });
		Assert.That(setup.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

		// Removing every IHostedService (below) also removes IntegrationStartupBackgroundService, which
		// normally discovers and registers every built-in integration. The fixture script's flow only
		// needs the Variables integration, so that one is registered and initialized directly instead of
		// paying for full integration discovery in every test run.
		using var scope = _host.Services.CreateScope();
		var registry = scope.ServiceProvider.GetRequiredService<IIntegrationRegistry>();
		var initializer = scope.ServiceProvider.GetRequiredService<IntegrationInitializer>();
		var variablesIntegration = new VariablesIntegration();
		var registration = await registry.RegisterAsync(variablesIntegration);
		Assert.That(registration.Registered, Is.True, registration.Describe());
		await initializer.InitializeAsync(variablesIntegration, "Variables");
	}

	[OneTimeTearDown]
	public async Task OneTimeTearDown()
	{
		_client.Dispose();
		await _host.StopAsync();
		_host.Dispose();
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _previousDataDir);

		if (Directory.Exists(_dataDir))
		{
			Directory.Delete(_dataDir, recursive: true);
		}
	}

	[Test]
	public async Task A_supplied_value_reaches_the_running_script_over_the_real_http_hop()
	{
		await CreateOutputVariable("applied_scene", "text", "Starting Soon");
		await CreateOutputVariable("applied_volume", "numeric", "5");
		await CreateOutputVariable("applied_muted", "boolean", "false");

		var scriptId = await CreateScriptOne();

		var run = await ReadJson(await Send(HttpMethod.Post,
			$"/api/scripts/{scriptId}/run",
			new { inputs = new { scene = "BRB", volume = 70, muted = true } }));

		var variables = await ReadJson(await Send(HttpMethod.Get, "/api/variables"));

		Assert.Multiple(() =>
		{
			Assert.That(run.GetProperty("success").GetBoolean(), Is.True, run.ToString());
			Assert.That(run.GetProperty("status").GetString(), Is.EqualTo("Succeeded"));
			Assert.That(run.GetProperty("appliedInputs").EnumerateArray().Select(v => v.GetString()),
				Is.EquivalentTo(_expectedAppliedInputs));

			Assert.That(ValueOf(variables, "applied_scene"), Is.EqualTo("BRB"));
			Assert.That(ValueOf(variables, "applied_volume"), Is.EqualTo("70"));
			Assert.That(ValueOf(variables, "applied_muted"), Is.EqualTo("true"));
		});
	}

	private static string? ValueOf(JsonElement variables, string name)
		=> variables.GetProperty("variables")
			.EnumerateArray()
			.First(v => v.GetProperty("name").GetString() == name)
			.GetProperty("value")
			.GetString();

	private async Task CreateOutputVariable(string name, string type, string initialValue)
	{
		var created = await ReadJson(await Send(HttpMethod.Post,
			"/api/variables",
			new { name, scope = "global", type, initialValue }));
		Assert.That(created.GetProperty("success").GetBoolean(), Is.True, created.ToString());
	}

	private async Task<string> CreateScriptOne()
	{
		const string flows = """
							 [
							   {
							     "triggerId": "onRun",
							     "triggerType": "onRun",
							     "children": [
							       {
							         "id": "b1",
							         "type": "action",
							         "blockType": "app.macro-deck.variables.set-variable",
							         "integrationId": "app.macro-deck.variables",
							         "actionId": "set-variable",
							         "parameters": [
							           { "name": "variable", "type": "string", "value": "applied_scene" },
							           { "name": "operation", "type": "string", "value": "set" },
							           { "name": "value", "type": "string", "value": { "$var": "scene" } }
							         ]
							       },
							       {
							         "id": "b2",
							         "type": "action",
							         "blockType": "app.macro-deck.variables.set-variable",
							         "integrationId": "app.macro-deck.variables",
							         "actionId": "set-variable",
							         "parameters": [
							           { "name": "variable", "type": "string", "value": "applied_volume" },
							           { "name": "operation", "type": "string", "value": "set" },
							           { "name": "value", "type": "string", "value": { "$var": "volume" } }
							         ]
							       },
							       {
							         "id": "b3",
							         "type": "action",
							         "blockType": "app.macro-deck.variables.set-variable",
							         "integrationId": "app.macro-deck.variables",
							         "actionId": "set-variable",
							         "parameters": [
							           { "name": "variable", "type": "string", "value": "applied_muted" },
							           { "name": "operation", "type": "string", "value": "set" },
							           { "name": "value", "type": "string", "value": { "$var": "muted" } }
							         ]
							       }
							     ]
							   }
							 ]
							 """;

		var created = await ReadJson(await Send(HttpMethod.Post,
			"/api/scripts",
			new
			{
				name = "Script One",
				flows,
				inputs = new object[]
				{
					new { name = "scene", type = "text", required = true, defaultValue = "Starting Soon" },
					new { name = "volume", type = "numeric", required = false, defaultValue = "5" },
					new { name = "muted", type = "boolean", required = false, defaultValue = "false" }
				}
			}));

		Assert.That(created.GetProperty("success").GetBoolean(), Is.True, created.ToString());
		return created.GetProperty("script").GetProperty("id").GetString()!;
	}

	private Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body = null)
	{
		var request = new HttpRequestMessage(method, path);
		if (body is not null)
		{
			request.Content = JsonContent.Create(body);
		}

		request.Headers.Add(LoopbackHeader, "1");
		return _client.SendAsync(request);
	}

	private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
		=> JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

	private static StartupReadiness CompletedStartupReadiness()
	{
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();

		return readiness;
	}

	private sealed class LoopbackStartupFilter : IStartupFilter
	{
		public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
			=> app =>
			{
				app.Use(async (context, nextMiddleware) =>
				{
					if (context.Request.Headers.ContainsKey(LoopbackHeader))
					{
						context.Connection.LocalPort = TestListenerPorts.Loopback;
						context.Connection.RemoteIpAddress = IPAddress.Loopback;
					}
					else
					{
						context.Connection.LocalPort = HostEndpoints.PublicPort;
						context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
					}

					await nextMiddleware();
				});
				next(app);
			};
	}
}
