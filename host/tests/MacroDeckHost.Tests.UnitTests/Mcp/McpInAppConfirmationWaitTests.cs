using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Mcp;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Mcp;
using MacroDeckHost.Tests.UnitTests.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Mcp;

public class McpInAppConfirmationWaitTests
{
	private const string TestIntegration = "test.mcp.wait";
	private const string SendAction = "send";

	private readonly CapturingActionDefinition _send = new()
	{
		Id = SendAction,
		Parameters = [ActionParameter.Text("target", required: true), ActionParameter.Text("mode")]
	};

	private readonly ManualTimeProvider _time = new() { Now = DateTimeOffset.UtcNow };

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
					services.AddSingleton<ILogLevelState>(new LogLevelState(LogEntryLevel.Information));
					services.AddSingleton<IStartupFilter, FakeConnectionStartupFilter>();
					services.RemoveAll<StartupReadiness>();
					var readiness = new StartupReadiness();
					readiness.MarkCachesReady();
					readiness.MarkVariablesReady();
					services.AddSingleton(readiness);
					services.RemoveAll<TimeProvider>();
					services.AddSingleton<TimeProvider>(_time);
				});
			})
			.StartAsync();

		_client = _host.GetTestClient();
		await _host.Services.GetRequiredService<IIntegrationRegistry>()
			.RegisterAsync(new FakeIntegration { Id = TestIntegration, Actions = [_send] });
		var policy = await Send(HttpMethod.Put,
			"/api/mcp/policy",
			new { integrationId = TestIntegration, mode = nameof(McpRiskMode.Confirm) });
		policy.EnsureSuccessStatusCode();
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
			Directory.Delete(_dataDir, true);
		}
	}

	[Test]
	public async Task An_answer_given_after_the_wait_is_picked_up_by_the_same_call_retried_once()
	{
		await using var client = await Connect();
		var executedBefore = _send.ExecuteCount;

		var first = await CallAfterTheWait(client,
			new Dictionary<string, object?> { ["target"] = "late", ["mode"] = "a" });
		var id = (await Pending()).Single().GetProperty("id").GetString();
		await Send(HttpMethod.Post, $"/api/mcp/confirmations/{id}/allow", new { });
		var retried = await Run(client,
			new Dictionary<string, object?> { ["mode"] = "a", ["target"] = "late" });
		var executedAfterRetry = _send.ExecuteCount;
		var again = Run(client, new Dictionary<string, object?> { ["target"] = "late", ["mode"] = "a" });
		var askedAgain = await WaitForPending();
		await Send(HttpMethod.Post,
			$"/api/mcp/confirmations/{askedAgain.GetProperty("id").GetString()}/decline",
			new { });
		await again;

		await Assert.MultipleAsync(() =>
		{
			Assert.That(JsonSerializer.Serialize(first), Does.Contain(McpConsent.Pending));
			Assert.That(retried.IsError, Is.Not.True, JsonSerializer.Serialize(retried));
			Assert.That(executedAfterRetry, Is.EqualTo(executedBefore + 1));
			Assert.That(askedAgain.GetProperty("id").GetString(), Is.Not.EqualTo(id));
			Assert.That(_send.ExecuteCount, Is.EqualTo(executedBefore + 1));
			return Task.CompletedTask;
		});
	}

	[Test]
	public async Task A_request_nobody_answers_expires_and_a_later_retry_asks_again()
	{
		await using var client = await Connect();
		var executedBefore = _send.ExecuteCount;
		var arguments = new Dictionary<string, object?> { ["target"] = "never" };

		var first = await CallAfterTheWait(client, arguments);
		var expired = (await Pending()).Single().GetProperty("id").GetString();
		_time.Advance(McpConfirmationStore.Lifetime);
		var afterExpiry = await Pending();
		var retry = Run(client, arguments);
		var askedAgain = await WaitForPending();
		await Send(HttpMethod.Post,
			$"/api/mcp/confirmations/{askedAgain.GetProperty("id").GetString()}/decline",
			new { });
		var declined = await retry;

		await Assert.MultipleAsync(() =>
		{
			Assert.That(JsonSerializer.Serialize(first), Does.Contain(McpConsent.Pending));
			Assert.That(afterExpiry, Is.Empty);
			Assert.That(askedAgain.GetProperty("id").GetString(), Is.Not.EqualTo(expired));
			Assert.That(JsonSerializer.Serialize(declined), Does.Contain(McpConsent.Declined));
			Assert.That(_send.ExecuteCount, Is.EqualTo(executedBefore));
			return Task.CompletedTask;
		});
	}

	private async Task<CallToolResult> CallAfterTheWait(McpClient client, Dictionary<string, object?> parameters)
	{
		var scheduled = _time.ScheduledCount;
		var call = Run(client, parameters);
		await WaitForPending();
		// A new request arms two timers: its expiry in the store and the call's wait in the gate.
		await _time.WaitForScheduleAsync(scheduled + 1);
		_time.Advance(McpConfirmationGate.InAppWait);
		return await call;
	}

	private static Task<CallToolResult> Run(McpClient client, Dictionary<string, object?> parameters)
	{
		return client.CallToolAsync("run_action",
				new Dictionary<string, object?>
				{
					["integrationId"] = TestIntegration,
					["actionId"] = SendAction,
					["parameters"] = parameters
				})
			.AsTask();
	}

	private async Task<IReadOnlyList<JsonElement>> Pending()
	{
		var response = await Send(HttpMethod.Get, "/api/mcp/confirmations");
		return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("confirmations")
			.EnumerateArray()
			.ToList();
	}

	private async Task<JsonElement> WaitForPending()
	{
		var deadline = DateTime.UtcNow.AddSeconds(10);
		while (DateTime.UtcNow < deadline)
		{
			var pending = await Pending();
			if (pending.Count > 0)
			{
				return pending[^1];
			}

			await Task.Delay(20);
		}

		Assert.Fail("No request appeared in Macro Deck.");
		return default;
	}

	private async Task<McpClient> Connect()
	{
		var token = await Send(HttpMethod.Post, "/api/mcp/tokens", new { name = "wait client" });
		var plaintext = (await token.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("plaintext").GetString();
		var http = new HttpClient(new McpShapeHandler(_host.GetTestServer().CreateHandler()))
		{
			BaseAddress = new Uri("http://localhost")
		};
		http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", plaintext);
		var transport = new HttpClientTransport(new HttpClientTransportOptions
			{
				Endpoint = new Uri("http://localhost/mcp"),
				TransportMode = HttpTransportMode.StreamableHttp
			},
			http,
			ownsHttpClient: true);

		return await McpClient.CreateAsync(transport,
			new McpClientOptions
			{
				ClientInfo = new Implementation { Name = "wait-client", Version = "1.0" },
				ProtocolVersion = "2025-11-25",
				Capabilities = new ClientCapabilities()
			});
	}

	private async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body = null)
	{
		using var request = new HttpRequestMessage(method, path);
		request.Headers.Add(FakeConnectionStartupFilter.ShapeHeader, nameof(FakeConnectionShape.Loopback));
		if (body is not null)
		{
			request.Content = JsonContent.Create(body);
		}

		return await _client.SendAsync(request);
	}

	private sealed class McpShapeHandler : DelegatingHandler
	{
		public McpShapeHandler(HttpMessageHandler inner)
			: base(inner)
		{
		}

		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			request.Headers.Add(FakeConnectionStartupFilter.ShapeHeader,
				nameof(FakeConnectionShape.LoopbackWithoutCredential));
			return base.SendAsync(request, cancellationToken);
		}
	}
}
