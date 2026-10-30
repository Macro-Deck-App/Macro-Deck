using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Mcp;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Persistence;
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

[NonParallelizable]
public partial class McpEndpointTests
{
	private const string LegacyProtocol = "2025-11-25";
	private static readonly ElicitationCapability _noElicitation = new();
	private const string SecretValue = "correct-horse-battery-staple";

	private static readonly string[] _mcpContractTools =
	[
		"catalog", "list", "get", "set_secret", "apply_changes", "undo", "run_action", "run_script", "trigger_event"
	];

	private IHost _host = null!;
	private HttpClient _client = null!;
	private string _dataDir = null!;
	private string? _previousDataDir;
	private string _adminToken = null!;

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
				});
			})
			.StartAsync();

		_client = _host.GetTestClient();

		var setup = await Send(HttpMethod.Post,
			"/api/auth/setup",
			FakeConnectionShape.Loopback,
			body: new { username = "admin", password = "password123" });
		Assert.That(setup.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

		var login = await Send(HttpMethod.Post,
			"/api/auth/login",
			FakeConnectionShape.PublicLan,
			body: new { username = "admin", password = "password123", scope = "admin" });
		_adminToken = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
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
	public async Task The_endpoint_accepts_only_a_live_mcp_token()
	{
		var token = await CreateToken();
		var revoked = await CreateToken();
		await Send(HttpMethod.Post, $"/api/mcp/tokens/{revoked.Id}/revoke", FakeConnectionShape.Loopback);

		var anonymous = await PostInitialize(null);
		var admin = await PostInitialize(_adminToken);
		var revokedResponse = await PostInitialize(revoked.Plaintext);
		var unknown = await PostInitialize(McpTokenService.TokenPrefix + new string('0', 64));
		var loopbackDesktop = await PostInitialize(null, FakeConnectionShape.Loopback);
		var live = await PostInitialize(token.Plaintext);

		await Assert.MultipleAsync(() =>
		{
			Assert.That(anonymous.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(admin.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(revokedResponse.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(loopbackDesktop.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(live.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			return Task.CompletedTask;
		});
	}

	[Test]
	public async Task An_mcp_token_opens_nothing_outside_the_mcp_endpoint()
	{
		var token = await CreateToken();

		var folders = await Send(HttpMethod.Get,
			"/api/folders",
			FakeConnectionShape.LoopbackWithoutCredential,
			token.Plaintext);
		var secrets = await Send(HttpMethod.Post,
			$"/api/secrets/{Guid.NewGuid()}/reveal",
			FakeConnectionShape.LoopbackWithoutCredential,
			token.Plaintext);
		var tokens = await Send(HttpMethod.Get,
			"/api/mcp/tokens",
			FakeConnectionShape.LoopbackWithoutCredential,
			token.Plaintext);

		await Assert.MultipleAsync(() =>
		{
			Assert.That(folders.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(secrets.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(tokens.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			return Task.CompletedTask;
		});
	}

	[Test]
	public async Task Browser_pages_cannot_reach_the_endpoint_on_any_listener()
	{
		var token = await CreateToken();

		var foreignOrigin = await PostInitialize(token.Plaintext, origin: "http://evil.example");
		var crossSite = await PostInitialize(token.Plaintext, secFetchSite: "cross-site");
		var rebound = await PostInitialize(token.Plaintext, host: "rebind.example");
		var publicCrossSite = await PostInitialize(token.Plaintext,
			FakeConnectionShape.PublicLan,
			secFetchSite: "cross-site");
		var publicForeignOrigin = await PostInitialize(token.Plaintext,
			FakeConnectionShape.PublicLan,
			"http://evil.example");
		var lanOnPrivatePort = await PostInitialize(token.Plaintext, FakeConnectionShape.LanOnPrivatePort);
		using var preflightRequest = new HttpRequestMessage(HttpMethod.Options, "/mcp");
		preflightRequest.Headers.Add(FakeConnectionStartupFilter.ShapeHeader, nameof(FakeConnectionShape.PublicLan));
		preflightRequest.Headers.Add("Origin", "http://evil.example");
		preflightRequest.Headers.Add("Access-Control-Request-Method", "POST");
		preflightRequest.Headers.Add("Access-Control-Request-Headers", "authorization");
		using var preflight = await _client.SendAsync(preflightRequest);
		var page = await Send(HttpMethod.Get,
			"/mcp/secret-entry/0123456789abcdef0123456789abcdef",
			FakeConnectionShape.LoopbackWithoutCredential,
			origin: "http://evil.example");
		var confirmation = await Send(HttpMethod.Post,
			"/mcp/confirm/0123456789abcdef0123456789abcdef",
			FakeConnectionShape.PublicLan,
			origin: "http://evil.example");

		await Assert.MultipleAsync(() =>
		{
			Assert.That(confirmation.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(foreignOrigin.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(crossSite.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(rebound.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(publicCrossSite.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(publicForeignOrigin.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(lanOnPrivatePort.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
			Assert.That(preflight.Headers.Contains("Access-Control-Allow-Origin"), Is.False);
			Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			return Task.CompletedTask;
		});
	}

	[Test]
	public async Task A_client_elsewhere_in_the_network_connects_with_a_token_on_the_public_port()
	{
		var token = await CreateToken();

		var withToken = await PostInitialize(token.Plaintext, FakeConnectionShape.PublicLan, host: "192.168.1.20:8191");
		var withoutToken = await PostInitialize(null, FakeConnectionShape.PublicLan, host: "192.168.1.20:8191");
		var endpoint = await Send(HttpMethod.Get, "/api/mcp/endpoint", FakeConnectionShape.Loopback);
		var url = (await endpoint.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString();

		await Assert.MultipleAsync(() =>
		{
			Assert.That(withToken.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(withoutToken.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(url, Is.EqualTo($"http://127.0.0.1:{HostEndpoints.PublicPort}/mcp"));
			return Task.CompletedTask;
		});
	}

	[TestCase(LegacyProtocol)]
	[TestCase(null)]
	public async Task A_client_sees_the_documented_tools_and_can_read_the_catalog(string? protocolVersion)
	{
		var token = await CreateToken();
		await using var client = await Connect(token.Plaintext, protocolVersion);

		var tools = await client.ListToolsAsync();
		var catalog = await client.CallToolAsync("catalog", new Dictionary<string, object?> { ["kind"] = "actions" });
		var actions = await (await Send(HttpMethod.Get, "/api/actions", FakeConnectionShape.Loopback))
			.Content.ReadFromJsonAsync<JsonElement>();
		var profiles = await client.CallToolAsync("list", new Dictionary<string, object?> { ["entity"] = "profiles" });

		await Assert.MultipleAsync(() =>
		{
			Assert.That(tools.Select(tool => tool.Name), Is.EquivalentTo(_mcpContractTools));
			Assert.That(catalog.IsError, Is.Not.True);
			Assert.That(catalog.StructuredContent!.Value.GetProperty("actions").GetArrayLength(),
				Is.EqualTo(actions.GetProperty("actions").GetArrayLength()));
			Assert.That(profiles.IsError, Is.Not.True);
			Assert.That(profiles.StructuredContent!.Value.GetProperty("entity").GetString(), Is.EqualTo("profiles"));
			return Task.CompletedTask;
		});
	}

	[Test]
	public async Task Revoking_a_token_cuts_off_its_open_session()
	{
		var token = await CreateToken();
		await using var client = await Connect(token.Plaintext, LegacyProtocol);
		await client.ListToolsAsync();

		await Send(HttpMethod.Post, $"/api/mcp/tokens/{token.Id}/revoke", FakeConnectionShape.Loopback);

		Assert.That(async () => await client.ListToolsAsync(), Throws.Exception);
	}

	[Test]
	public async Task A_session_cannot_be_driven_with_another_token()
	{
		var owner = await CreateToken();
		var intruder = await CreateToken();
		await using var client = await Connect(owner.Plaintext, LegacyProtocol);
		await client.ListToolsAsync();

		var hijack = await PostJsonRpc(intruder.Plaintext,
			new { jsonrpc = "2.0", id = 2, method = "tools/list" },
			client.SessionId);

		Assert.That(hijack.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
	}

	[TestCase(LegacyProtocol, false)]
	[TestCase(null, false)]
	[TestCase(LegacyProtocol, true)]
	[TestCase(null, true)]
	public async Task Set_secret_returns_only_a_reference_to_what_the_user_typed_on_the_page(
		string? protocolVersion,
		bool typesAfterConsenting)
	{
		var token = await CreateToken();
		string? url = null;
		var typing = Task.CompletedTask;
		await using var client = await Connect(token.Plaintext,
			protocolVersion,
			async (request, _) =>
			{
				url = request?.Url;
				if (typesAfterConsenting)
				{
					typing = Task.Run(async () =>
						{
							await Task.Delay(200);
							await TypeSecretOnPage(request!.Url!);
						},
						CancellationToken.None);
				}
				else
				{
					await TypeSecretOnPage(request!.Url!);
				}

				return new ElicitResult { Action = "accept" };
			});

		var result = await client.CallToolAsync("set_secret",
			new Dictionary<string, object?> { ["kind"] = "password", ["label"] = "OBS password" });

		await typing;
		var raw = JsonSerializer.Serialize(result);
		Assert.That(result.IsError, Is.Not.True, raw);
		var secretId = result.StructuredContent!.Value.GetProperty("secret").GetProperty("$secret").GetGuid();
		using var scope = _host.Services.CreateScope();
		var stored = await scope.ServiceProvider.GetRequiredService<ISecretService>().Resolve(secretId);
		var reused = await Send(HttpMethod.Get,
			new Uri(url!).PathAndQuery,
			FakeConnectionShape.LoopbackWithoutCredential);

		await Assert.MultipleAsync(() =>
		{
			Assert.That(result.IsError, Is.Not.True);
			Assert.That(raw, Does.Not.Contain(SecretValue));
			Assert.That(stored, Is.EqualTo(SecretValue));
			Assert.That(url, Does.StartWith("http://localhost/mcp/secret-entry/"));
			Assert.That(reused.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
			return Task.CompletedTask;
		});
	}

	[Test]
	public async Task Set_secret_stores_nothing_when_the_user_declines()
	{
		var token = await CreateToken();
		await using var client = await Connect(token.Plaintext,
			LegacyProtocol,
			(_, _) => ValueTask.FromResult(new ElicitResult { Action = "decline" }));

		var result = await client.CallToolAsync("set_secret",
			new Dictionary<string, object?> { ["kind"] = "secret", ["label"] = "API key" });

		Assert.That(result.IsError, Is.True);
	}

	[Test]
	public async Task The_token_list_reports_created_and_last_used_in_utc_and_in_order()
	{
		var created = await CreateToken();
		await using (var client = await Connect(created.Plaintext, null))
		{
			await client.ListToolsAsync();
		}

		var list = await Send(HttpMethod.Get, "/api/mcp/tokens", FakeConnectionShape.Loopback);
		var entry = (await list.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tokens")
			.EnumerateArray()
			.Single(token => token.GetProperty("id").GetGuid() == created.Id);
		var createdAt = entry.GetProperty("createdAt").GetString()!;
		var lastUsedAt = entry.GetProperty("lastUsedAt").GetString()!;

		await Assert.MultipleAsync(() =>
		{
			Assert.That(createdAt, Does.EndWith("Z"));
			Assert.That(lastUsedAt, Does.EndWith("Z"));
			Assert.That(DateTimeOffset.Parse(createdAt, CultureInfo.InvariantCulture),
				Is.LessThanOrEqualTo(DateTimeOffset.Parse(lastUsedAt, CultureInfo.InvariantCulture)));
			Assert.That(DateTimeOffset.Parse(createdAt, CultureInfo.InvariantCulture),
				Is.EqualTo(DateTimeOffset.UtcNow).Within(TimeSpan.FromMinutes(1)));
			return Task.CompletedTask;
		});
	}

	[Test]
	public async Task Token_admin_endpoints_create_list_revoke_and_delete()
	{
		var created = await CreateToken();
		var notRevokedDelete
			= await Send(HttpMethod.Delete, $"/api/mcp/tokens/{created.Id}", FakeConnectionShape.Loopback);
		await Send(HttpMethod.Post, $"/api/mcp/tokens/{created.Id}/revoke", FakeConnectionShape.Loopback);
		var listed = await (await Send(HttpMethod.Get, "/api/mcp/tokens", FakeConnectionShape.Loopback))
			.Content.ReadFromJsonAsync<JsonElement>();
		var deleted = await Send(HttpMethod.Delete, $"/api/mcp/tokens/{created.Id}", FakeConnectionShape.Loopback);
		var afterDelete = await (await Send(HttpMethod.Get, "/api/mcp/tokens", FakeConnectionShape.Loopback))
			.Content.ReadFromJsonAsync<JsonElement>();

		var entry = listed.GetProperty("tokens")
			.EnumerateArray()
			.Single(token => token.GetProperty("id").GetGuid() == created.Id);
		var notRevokedBody = await notRevokedDelete.Content.ReadFromJsonAsync<JsonElement>();
		var deletedBody = await deleted.Content.ReadFromJsonAsync<JsonElement>();

		await Assert.MultipleAsync(() =>
		{
			Assert.That(created.Plaintext, Does.StartWith(McpTokenService.TokenPrefix));
			Assert.That(entry.GetRawText(), Does.Not.Contain(created.Plaintext));
			Assert.That(entry.GetProperty("revokedAt").ValueKind, Is.Not.EqualTo(JsonValueKind.Null));
			Assert.That(notRevokedBody.GetProperty("success").GetBoolean(), Is.False);
			Assert.That(deletedBody.GetProperty("success").GetBoolean(), Is.True);
			Assert.That(afterDelete.GetProperty("tokens")
					.EnumerateArray()
					.Any(token => token.GetProperty("id").GetGuid() == created.Id),
				Is.False);
			return Task.CompletedTask;
		});
	}

	private async Task TypeSecretOnPage(string url)
	{
		var path = new Uri(url).PathAndQuery;
		var form = await Send(HttpMethod.Get, path, FakeConnectionShape.LoopbackWithoutCredential);
		var html = await form.Content.ReadAsStringAsync();
		var nonce = NonceField().Match(html).Groups[1].Value;

		using var request = new HttpRequestMessage(HttpMethod.Post, path);
		request.Headers.Add(FakeConnectionStartupFilter.ShapeHeader,
			nameof(FakeConnectionShape.LoopbackWithoutCredential));
		request.Headers.Add("Origin", "http://localhost");
		request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["nonce"] = nonce,
			["value"] = SecretValue
		});
		var saved = await _client.SendAsync(request);
		Assert.That(saved.StatusCode, Is.EqualTo(HttpStatusCode.OK));
	}

	private async Task<McpClient> Connect(
		string token,
		string? protocolVersion,
		Func<ElicitRequestParams?, CancellationToken, ValueTask<ElicitResult>>? elicitation = null,
		ElicitationCapability? capability = null)
	{
		var http = new HttpClient(new ShapeHandler(_host.GetTestServer().CreateHandler()))
		{
			BaseAddress = new Uri("http://localhost")
		};
		http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

		var transport = new HttpClientTransport(new HttpClientTransportOptions
			{
				Endpoint = new Uri("http://localhost/mcp"),
				TransportMode = HttpTransportMode.StreamableHttp
			},
			http,
			ownsHttpClient: true);

		var options = new McpClientOptions
		{
			ClientInfo = new Implementation { Name = "test-client", Version = "1.0" },
			ProtocolVersion = protocolVersion,
			Capabilities = new ClientCapabilities
			{
				Elicitation = ReferenceEquals(capability, _noElicitation)
					? null
					: capability
					?? new ElicitationCapability
						{ Url = new UrlElicitationCapability(), Form = new FormElicitationCapability() }
			},
			Handlers = new McpClientHandlers { ElicitationHandler = elicitation }
		};

		return await McpClient.CreateAsync(transport, options);
	}

	private async Task<(Guid Id, string Plaintext)> CreateToken()
	{
		var response = await Send(HttpMethod.Post,
			"/api/mcp/tokens",
			FakeConnectionShape.Loopback,
			body: new { name = "test client" });
		var json = await response.Content.ReadFromJsonAsync<JsonElement>();
		return (json.GetProperty("token").GetProperty("id").GetGuid(), json.GetProperty("plaintext").GetString()!);
	}

	private Task<HttpResponseMessage> PostInitialize(
		string? token,
		FakeConnectionShape shape = FakeConnectionShape.LoopbackWithoutCredential,
		string? origin = null,
		string? secFetchSite = null,
		string? host = null)
	{
		return PostJsonRpc(token,
			new
			{
				jsonrpc = "2.0",
				id = 1,
				method = "initialize",
				@params = new
				{
					protocolVersion = LegacyProtocol,
					capabilities = new { },
					clientInfo = new { name = "probe", version = "1" }
				}
			},
			null,
			shape,
			origin,
			secFetchSite,
			host);
	}

	private async Task<HttpResponseMessage> PostJsonRpc(
		string? token,
		object message,
		string? sessionId,
		FakeConnectionShape shape = FakeConnectionShape.LoopbackWithoutCredential,
		string? origin = null,
		string? secFetchSite = null,
		string? host = null)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
		request.Headers.Add(FakeConnectionStartupFilter.ShapeHeader, shape.ToString());
		request.Headers.Accept.ParseAdd("application/json");
		request.Headers.Accept.ParseAdd("text/event-stream");
		if (token is not null)
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		}

		if (sessionId is not null)
		{
			request.Headers.Add("Mcp-Session-Id", sessionId);
		}

		if (origin is not null)
		{
			request.Headers.Add("Origin", origin);
		}

		if (secFetchSite is not null)
		{
			request.Headers.Add("Sec-Fetch-Site", secFetchSite);
		}

		if (host is not null)
		{
			request.Headers.Host = host;
		}

		request.Content = JsonContent.Create(message);
		return await _client.SendAsync(request);
	}

	private async Task<HttpResponseMessage> Send(
		HttpMethod method,
		string path,
		FakeConnectionShape shape,
		string? bearerToken = null,
		object? body = null,
		string? origin = null)
	{
		using var request = new HttpRequestMessage(method, path);
		request.Headers.Add(FakeConnectionStartupFilter.ShapeHeader, shape.ToString());
		if (bearerToken is not null)
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
		}

		if (origin is not null)
		{
			request.Headers.Add("Origin", origin);
		}

		if (body is not null)
		{
			request.Content = JsonContent.Create(body);
		}

		return await _client.SendAsync(request);
	}

	[GeneratedRegex("name=\"nonce\" value=\"([0-9a-f]+)\"")]
	private static partial Regex NonceField();

	private sealed class ShapeHandler : DelegatingHandler
	{
		public ShapeHandler(HttpMessageHandler inner)
			: base(inner)
		{
		}

		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			request.Headers.Remove(FakeConnectionStartupFilter.ShapeHeader);
			request.Headers.Add(FakeConnectionStartupFilter.ShapeHeader,
				nameof(FakeConnectionShape.LoopbackWithoutCredential));
			return base.SendAsync(request, cancellationToken);
		}
	}
}
