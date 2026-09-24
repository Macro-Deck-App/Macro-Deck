using System.Net;
using System.Net.Http.Headers;
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

[NonParallelizable]
public class CompanionScriptsEndpointTests
{
	private const string LoopbackHeader = "X-Test-Loopback";

	private IHost _host = null!;
	private HttpClient _client = null!;
	private string _dataDir = null!;
	private string? _previousDataDir;
	private string _clientToken = null!;
	private string _standaloneId = null!;
	private string _onWidgetId = null!;

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
			new { username = "admin", password = "password123" },
			loopback: true);
		Assert.That(setup.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

		using var scope = _host.Services.CreateScope();
		var registry = scope.ServiceProvider.GetRequiredService<IIntegrationRegistry>();
		var initializer = scope.ServiceProvider.GetRequiredService<IntegrationInitializer>();
		var variablesIntegration = new VariablesIntegration();
		var registration = await registry.RegisterAsync(variablesIntegration);
		Assert.That(registration.Registered, Is.True, registration.Describe());
		await initializer.InitializeAsync(variablesIntegration, "Variables");

		var login = await Send(HttpMethod.Post,
			"/api/auth/login",
			new { username = "admin", password = "password123", scope = "client" });
		Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		_clientToken = (await ReadJson(login)).GetProperty("accessToken").GetString()!;

		await CreateOutputVariable("companion_scene", "text", "unset");
		_standaloneId = await CreateScript("Set Scene", runsOnWidget: false);
		_onWidgetId = await CreateScript("On A Widget", runsOnWidget: true);
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
	public async Task A_client_lists_the_scripts_that_do_not_run_on_a_widget_without_their_flows()
	{
		var response = await Send(HttpMethod.Get, "/api/companion/scripts", token: _clientToken);
		var scripts = (await ReadJson(response)).GetProperty("scripts").EnumerateArray().ToList();

		Assert.Multiple(() =>
		{
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(scripts.Select(s => s.GetProperty("id").GetString()), Is.EqualTo(new[] { _standaloneId }));
			Assert.That(scripts[0].GetProperty("name").GetString(), Is.EqualTo("Set Scene"));
			Assert.That(scripts[0].TryGetProperty("flows", out _), Is.False);
			var input = scripts[0].GetProperty("inputs").EnumerateArray().Single();
			Assert.That(input.GetProperty("name").GetString(), Is.EqualTo("scene"));
			Assert.That(input.GetProperty("type").GetString(), Is.EqualTo("text"));
			Assert.That(input.GetProperty("required").GetBoolean(), Is.True);
		});
	}

	[Test]
	public async Task A_client_runs_a_script_with_its_input_values_and_gets_only_the_outcome()
	{
		var response = await Send(HttpMethod.Post,
			$"/api/companion/scripts/{_standaloneId}/run",
			new { inputs = new { scene = "Gaming" }, clientId = "companion-1" },
			token: _clientToken);
		var run = await ReadJson(response);
		var scene = await SceneVariable();

		Assert.Multiple(() =>
		{
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(run.GetProperty("success").GetBoolean(), Is.True, run.ToString());
			Assert.That(run.GetProperty("status").GetString(), Is.EqualTo("Succeeded"));
			Assert.That(run.TryGetProperty("actions", out _), Is.False);
			Assert.That(scene, Is.EqualTo("Gaming"));
		});
	}

	[Test]
	public async Task A_script_that_runs_on_a_widget_is_not_found_and_does_not_run()
	{
		await SetSceneVariable("before");

		var run = await ReadJson(await Send(HttpMethod.Post,
			$"/api/companion/scripts/{_onWidgetId}/run",
			new { inputs = new { scene = "Gaming" } },
			token: _clientToken));
		var scene = await SceneVariable();

		Assert.Multiple(() =>
		{
			Assert.That(run.GetProperty("success").GetBoolean(), Is.False);
			Assert.That(run.GetProperty("error").GetProperty("code").GetString(), Is.EqualTo("NotFound"));
			Assert.That(scene, Is.EqualTo("before"));
		});
	}

	[Test]
	public async Task A_missing_required_input_fails_with_the_binders_error()
	{
		var run = await ReadJson(await Send(HttpMethod.Post,
			$"/api/companion/scripts/{_standaloneId}/run",
			new { inputs = new { } },
			token: _clientToken));

		Assert.Multiple(() =>
		{
			Assert.That(run.GetProperty("success").GetBoolean(), Is.False);
			Assert.That(run.GetProperty("error").GetProperty("code").GetString(), Is.EqualTo("SCRIPT_INPUT_MISSING"));
		});
	}

	[Test]
	public async Task The_admin_scripts_routes_stay_closed_to_a_client_and_the_new_ones_to_anonymous_callers()
	{
		var adminList = await Send(HttpMethod.Get, "/api/scripts", token: _clientToken);
		var adminRun = await Send(HttpMethod.Post, $"/api/scripts/{_standaloneId}/run", new { }, token: _clientToken);
		var anonymousList = await Send(HttpMethod.Get, "/api/companion/scripts");
		var anonymousRun = await Send(HttpMethod.Post, $"/api/companion/scripts/{_standaloneId}/run", new { });

		Assert.Multiple(() =>
		{
			Assert.That(adminList.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(adminRun.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(anonymousList.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(anonymousRun.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
		});
	}

	private async Task<string?> SceneVariable()
	{
		var variables = await ReadJson(await Send(HttpMethod.Get, "/api/variables", loopback: true));
		return variables.GetProperty("variables")
			.EnumerateArray()
			.First(v => v.GetProperty("name").GetString() == "companion_scene")
			.GetProperty("value")
			.GetString();
	}

	private async Task SetSceneVariable(string value)
	{
		var variables = await ReadJson(await Send(HttpMethod.Get, "/api/variables", loopback: true));
		var id = variables.GetProperty("variables")
			.EnumerateArray()
			.First(v => v.GetProperty("name").GetString() == "companion_scene")
			.GetProperty("id")
			.GetString();
		var set = await Send(HttpMethod.Patch, $"/api/variables/{id}/value", new { id, value }, loopback: true);
		Assert.That(set.IsSuccessStatusCode, Is.True);
	}

	private async Task CreateOutputVariable(string name, string type, string initialValue)
	{
		var created = await ReadJson(await Send(HttpMethod.Post,
			"/api/variables",
			new { name, scope = "global", type, initialValue },
			loopback: true));
		Assert.That(created.GetProperty("success").GetBoolean(), Is.True, created.ToString());
	}

	private async Task<string> CreateScript(string name, bool runsOnWidget)
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
							           { "name": "variable", "type": "string", "value": "companion_scene" },
							           { "name": "operation", "type": "string", "value": "set" },
							           { "name": "value", "type": "string", "value": { "$var": "scene" } }
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
				name,
				flows,
				runsOnWidget,
				inputs = new object[] { new { name = "scene", type = "text", required = true } }
			},
			loopback: true));

		Assert.That(created.GetProperty("success").GetBoolean(), Is.True, created.ToString());
		return created.GetProperty("script").GetProperty("id").GetString()!;
	}

	private Task<HttpResponseMessage> Send(
		HttpMethod method,
		string path,
		object? body = null,
		string? token = null,
		bool loopback = false)
	{
		var request = new HttpRequestMessage(method, path);
		if (body is not null)
		{
			request.Content = JsonContent.Create(body);
		}

		if (token is not null)
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		}

		if (loopback)
		{
			request.Headers.Add(LoopbackHeader, "1");
		}

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
