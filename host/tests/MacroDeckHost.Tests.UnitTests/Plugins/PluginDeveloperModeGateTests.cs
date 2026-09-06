using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MacroDeck.Plugin.Protocol;
using MacroDeck.Plugin.Protocol.Auth;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Plugins;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Tests.UnitTests.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

/// <summary>Issue #753: Developer Mode is a kill switch, not an enrolment-time check. Everything here
/// runs against one host so switching the setting is exercised the way a user does it - live, with no
/// restart in between.</summary>
[NonParallelizable]
public class PluginDeveloperModeGateTests
{
	private IHost _host = null!;
	private HttpClient _client = null!;
	private string _dataDir = null!;
	private string? _previousDataDir;
	private string _adminToken = null!;
	private readonly RecordingUiTransport _uiTransport = new();

	// Showing that a refusal does not charge a throttle bucket takes more attempts than the bucket
	// tolerates, so the fixture configures the tolerance rather than assuming the host's default.
	private const int ThrottleFreeAttempts = 3;

	[OneTimeSetUp]
	public async Task OneTimeSetUp()
	{
		_dataDir = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		_previousDataDir = Environment.GetEnvironmentVariable("MACRODECK_DATA_DIR");
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _dataDir);

		var paths = new MacroDeckPaths();
		DatabaseMigrationHelper.MigrateDatabase(paths);

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
					services.AddSingleton(CompletedStartupReadiness());
					services.RemoveAll<IUiTransport>();
					services.AddSingleton<IUiTransport>(_uiTransport);
					services.RemoveAllKeyed<Application.Auth.LoginThrottle>("plugin");
					services.AddKeyedSingleton<Application.Auth.LoginThrottle>("plugin",
						(sp, _) => new Application.Auth.LoginThrottle(sp.GetRequiredService<TimeProvider>(),
							freeAttempts: ThrottleFreeAttempts));
				});
			})
			.StartAsync();

		_client = _host.GetTestClient();

		var setup = await Send(HttpMethod.Post,
			"/api/auth/setup",
			shape: FakeConnectionShape.Loopback,
			body: new { username = "admin", password = "password123" });
		Assert.That(setup.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

		var login = await Send(HttpMethod.Post,
			"/api/auth/login",
			shape: FakeConnectionShape.PublicLan,
			body: new { username = "admin", password = "password123", scope = "admin" });
		Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		_adminToken = (await ReadJson(login)).GetProperty("accessToken").GetString()!;
	}

	[SetUp]
	public void SetUp()
	{
		// The shared throttle buckets and the recording transport are singletons shared by every test in
		// this fixture; reset them so one test's refusals or events cannot be read by the next. The
		// session bucket is keyed per plugin id and every test mints its own, so it never carries over.
		var throttle = _host.Services.GetRequiredKeyedService<Application.Auth.LoginThrottle>("plugin");
		throttle.RegisterSuccess("plugin-enroll");
		throttle.RegisterSuccess("plugin-pairing");
		throttle.RegisterSuccess("plugin-pairing-redeem");
		_uiTransport.Broadcasts.Clear();
		_uiTransport.GroupMessages.Clear();
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
	public async Task Enrolment_is_forbidden_while_developer_mode_is_off_and_works_again_once_it_is_back_on()
	{
		await SetDeveloperMode(false);
		var token = await CreateTokenAsync();

		var refused = await EnrollAsync(NewPluginId(), token.Plaintext);
		var error = await ReadJson(refused);
		var registrations = await RegistrationsOfAsync(token.Id);

		Assert.Multiple(() =>
		{
			Assert.That(refused.StatusCode,
				Is.EqualTo(HttpStatusCode.Forbidden),
				"a valid credential offered to a host with the switch off is refused, not rejected as unknown");
			Assert.That(error.GetProperty("code").GetString(), Is.EqualTo(ProtocolErrorCodes.Unauthenticated));
			Assert.That(error.GetProperty("details").GetProperty("reason").GetString(),
				Is.EqualTo(ProtocolErrorReasons.DeveloperModeDisabled));
			Assert.That(registrations, Is.Empty);
		});

		await SetDeveloperMode(true);
		var accepted = await EnrollAsync(NewPluginId(), token.Plaintext);

		Assert.That(accepted.StatusCode, Is.EqualTo(HttpStatusCode.Created));
	}

	[Test]
	public async Task Enrolment_refusals_while_developer_mode_is_off_leave_the_shared_enrolment_bucket_open()
	{
		await SetDeveloperMode(false);
		var token = await CreateTokenAsync();

		for (var attempt = 0; attempt <= ThrottleFreeAttempts; attempt++)
		{
			var refused = await EnrollAsync(NewPluginId(), token.Plaintext);
			Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
		}

		await SetDeveloperMode(true);
		var accepted = await EnrollAsync(NewPluginId(), token.Plaintext);

		var guessed = HttpStatusCode.OK;
		for (var attempt = 0; attempt <= ThrottleFreeAttempts; attempt++)
		{
			guessed = (await EnrollAsync(NewPluginId(), "not-a-real-token")).StatusCode;
		}

		Assert.Multiple(() =>
		{
			Assert.That(accepted.StatusCode,
				Is.EqualTo(HttpStatusCode.Created),
				"the enrolment bucket is shared by every plugin, so a plugin retrying against a " +
				"switched-off host must not be able to lock the others out");
			Assert.That(guessed,
				Is.EqualTo(HttpStatusCode.TooManyRequests),
				"the same number of wrong-credential attempts does lock the bucket - without this the " +
				"assertion above would pass even if the loop never reached the limit");
		});
	}

	[Test]
	public async Task Session_refusals_while_developer_mode_is_off_leave_the_plugins_session_bucket_open()
	{
		await SetDeveloperMode(true);
		var token = await CreateTokenAsync();
		var pluginId = NewPluginId();
		var secret = await EnrollAndReadSecretAsync(pluginId, token.Plaintext);

		await SetDeveloperMode(false);
		for (var attempt = 0; attempt <= ThrottleFreeAttempts; attempt++)
		{
			var refused = await CreateSessionAsync(pluginId, secret);
			Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
		}

		await SetDeveloperMode(true);
		var accepted = await CreateSessionAsync(pluginId, secret);

		var guessed = HttpStatusCode.OK;
		for (var attempt = 0; attempt <= ThrottleFreeAttempts; attempt++)
		{
			guessed = (await CreateSessionAsync(pluginId, "not-the-real-secret")).StatusCode;
		}

		Assert.Multiple(() =>
		{
			Assert.That(accepted.StatusCode,
				Is.EqualTo(HttpStatusCode.Created),
				"a reconnect loop that ran while the switch was off must not have locked the plugin out " +
				"of the session it is entitled to once the switch is back on");
			Assert.That(guessed,
				Is.EqualTo(HttpStatusCode.TooManyRequests),
				"the same number of wrong-secret attempts does lock the bucket - without this the " +
				"assertion above would pass even if the loop never reached the limit");
		});
	}

	[Test]
	public async Task An_enrolled_plugins_reconnect_is_forbidden_while_developer_mode_is_off()
	{
		await SetDeveloperMode(true);
		var token = await CreateTokenAsync();
		var pluginId = NewPluginId();
		var secret = await EnrollAndReadSecretAsync(pluginId, token.Plaintext);
		Assert.That((await CreateSessionAsync(pluginId, secret)).StatusCode, Is.EqualTo(HttpStatusCode.Created));

		await SetDeveloperMode(false);
		var refused = await CreateSessionAsync(pluginId, secret);
		var error = await ReadJson(refused);

		Assert.Multiple(() =>
		{
			Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(error.GetProperty("details").GetProperty("reason").GetString(),
				Is.EqualTo(ProtocolErrorReasons.DeveloperModeDisabled));
		});

		await SetDeveloperMode(true);
		var allowed = await CreateSessionAsync(pluginId, secret);

		Assert.That(allowed.StatusCode, Is.EqualTo(HttpStatusCode.Created));
	}

	[Test]
	public async Task A_paired_plugins_reconnect_is_forbidden_while_developer_mode_is_off()
	{
		await SetDeveloperMode(true);
		var (pluginId, secret) = await PairPluginAsync();
		Assert.That((await CreateSessionAsync(pluginId, secret)).StatusCode, Is.EqualTo(HttpStatusCode.Created));

		await SetDeveloperMode(false);
		var refused = await CreateSessionAsync(pluginId, secret);
		var error = await ReadJson(refused);

		Assert.Multiple(() =>
		{
			Assert.That(refused.StatusCode,
				Is.EqualTo(HttpStatusCode.Forbidden),
				"a paired plugin owns no developer token, and is still development tooling");
			Assert.That(error.GetProperty("details").GetProperty("reason").GetString(),
				Is.EqualTo(ProtocolErrorReasons.DeveloperModeDisabled));
		});
	}

	[Test]
	public async Task Switching_developer_mode_off_ends_the_development_session_and_spares_the_managed_one()
	{
		await SetDeveloperMode(true);
		var token = await CreateTokenAsync();
		var developerPluginId = NewPluginId();
		var developerSecret = await EnrollAndReadSecretAsync(developerPluginId, token.Plaintext);
		using var developerSocket = await ConnectAsync(developerPluginId, developerSecret);

		var managedPluginId = NewPluginId();
		var launchSecret = _host.Services.GetRequiredService<IPluginLaunchTokenService>()
			.Mint(managedPluginId, $"launch-{Guid.NewGuid():N}", "Managed Plugin", "1.0.0");
		using var managedSocket = await ConnectAsync(managedPluginId, launchSecret);

		_uiTransport.GroupMessages.Clear();
		await SetDeveloperMode(false);

		var sessions = await ListSessionsAsync();
		var liveDeveloperSessions = sessions
			.Where(session => PluginIdOf(session) == developerPluginId &&
				session.GetProperty("state").GetString() != "dropped")
			.ToList();
		var managedSession = sessions.Single(session => PluginIdOf(session) == managedPluginId);
		var developerRegistrations = await RegistrationsOfAsync(token.Id);
		var refusedReconnect = await CreateSessionAsync(developerPluginId, developerSecret);

		Assert.Multiple(() =>
		{
			Assert.That(liveDeveloperSessions,
				Is.Empty,
				"the development session must not still be live after the switch went off");
			Assert.That(managedSession.GetProperty("state").GetString(), Is.EqualTo("connected"));
			Assert.That(managedSession.GetProperty("origin").GetString(), Is.EqualTo("managed"));
			Assert.That(_uiTransport.GroupMessages.Select(sent => sent.Message),
				Has.Some.InstanceOf<PluginSessionsChangedEvent>());

			Assert.That(developerRegistrations,
				Does.Contain(developerPluginId),
				"the switch stops the plugin, it does not throw the developer's enrolment away");
			Assert.That(refusedReconnect.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
		});
	}

	[Test]
	public async Task Turning_developer_mode_off_and_on_again_keeps_every_stored_credential()
	{
		await SetDeveloperMode(true);
		var token = await CreateTokenAsync();
		var enrolledPluginId = NewPluginId();
		var enrolledSecret = await EnrollAndReadSecretAsync(enrolledPluginId, token.Plaintext);
		var (pairedPluginId, _) = await PairPluginAsync();
		var createdAtBefore = await RegistrationCreatedAtAsync(token.Id, enrolledPluginId);

		await SetDeveloperMode(false);
		await SetDeveloperMode(true);

		var pairedRegistrations = await ListPairedRegistrationsAsync();
		var session = await CreateSessionAsync(enrolledPluginId, enrolledSecret);
		var createdAtAfter = await RegistrationCreatedAtAsync(token.Id, enrolledPluginId);

		Assert.Multiple(() =>
		{
			Assert.That(createdAtAfter, Is.EqualTo(createdAtBefore));
			Assert.That(pairedRegistrations, Does.Contain(pairedPluginId));
			Assert.That(session.StatusCode,
				Is.EqualTo(HttpStatusCode.Created),
				"the secret the plugin already holds must still work - the switch rotates nothing");
		});
	}

	[Test]
	public async Task The_new_developer_mode_value_reaches_ui_clients_and_an_unchanged_value_pushes_nothing()
	{
		await SetDeveloperMode(true);
		_uiTransport.Broadcasts.Clear();

		await SetDeveloperMode(false);
		var afterOff = DeveloperSettingsEvents();

		_uiTransport.Broadcasts.Clear();
		await SetDeveloperMode(false);
		var afterRepeat = DeveloperSettingsEvents();

		_uiTransport.Broadcasts.Clear();
		await SetDeveloperMode(true);
		var afterOn = DeveloperSettingsEvents();

		Assert.Multiple(() =>
		{
			Assert.That(afterOff, Has.Count.EqualTo(1));
			Assert.That(afterOff[0].Enabled, Is.False);
			Assert.That(afterRepeat, Is.Empty);
			Assert.That(afterOn, Has.Count.EqualTo(1));
			Assert.That(afterOn[0].Enabled, Is.True);
		});
	}

	private List<DeveloperSettingsChangedEvent> DeveloperSettingsEvents()
		=> _uiTransport.Broadcasts.OfType<DeveloperSettingsChangedEvent>().ToList();

	private static string PluginIdOf(JsonElement session) => session.GetProperty("pluginId").GetString()!;

	private static string NewPluginId() => $"com.example.d{Guid.NewGuid():N}";

	private async Task SetDeveloperMode(bool enabled)
	{
		var response = await Send(HttpMethod.Put,
			"/api/settings/developer",
			shape: FakeConnectionShape.PublicLan,
			bearerToken: _adminToken,
			body: new { enabled });
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
	}

	private async Task<(Guid Id, string Plaintext)> CreateTokenAsync()
	{
		var response = await Send(HttpMethod.Post,
			"/api/plugin-tokens",
			shape: FakeConnectionShape.PublicLan,
			bearerToken: _adminToken,
			body: new { name = $"token-{Guid.NewGuid():N}", expiresInDays = (int?)null });
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
		var json = await ReadJson(response);

		return (json.GetProperty("token").GetProperty("id").GetGuid(), json.GetProperty("plaintext").GetString()!);
	}

	private async Task<JsonElement> ReadTokenAsync(Guid id)
	{
		var response = await Send(HttpMethod.Get,
			"/api/plugin-tokens",
			shape: FakeConnectionShape.PublicLan,
			bearerToken: _adminToken);
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

		return (await ReadJson(response)).GetProperty("tokens").EnumerateArray()
			.Single(token => token.GetProperty("id").GetGuid() == id);
	}

	private async Task<IReadOnlyList<string>> RegistrationsOfAsync(Guid tokenId)
		=> (await ReadTokenAsync(tokenId)).GetProperty("registrations").EnumerateArray()
			.Select(registration => registration.GetProperty("pluginId").GetString()!)
			.ToList();

	private async Task<DateTime> RegistrationCreatedAtAsync(Guid tokenId, string pluginId)
		=> (await ReadTokenAsync(tokenId)).GetProperty("registrations").EnumerateArray()
			.Single(registration => registration.GetProperty("pluginId").GetString() == pluginId)
			.GetProperty("createdAt")
			.GetDateTime();

	private async Task<IReadOnlyList<string>> ListPairedRegistrationsAsync()
	{
		var response = await Send(HttpMethod.Get, "/api/plugin-pairing/registrations", FakeConnectionShape.Loopback);
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

		return (await ReadJson(response)).GetProperty("registrations").EnumerateArray()
			.Select(registration => registration.GetProperty("pluginId").GetString()!)
			.ToList();
	}

	private async Task<IReadOnlyList<JsonElement>> ListSessionsAsync()
	{
		var response = await Send(HttpMethod.Get,
			"/api/plugin-sessions",
			shape: FakeConnectionShape.PublicLan,
			bearerToken: _adminToken);
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

		return (await ReadJson(response)).GetProperty("sessions").EnumerateArray().ToList();
	}

	private Task<HttpResponseMessage> EnrollAsync(string pluginId, string enrollmentToken)
		=> Send(HttpMethod.Post,
			"/api/plugins/registration",
			shape: FakeConnectionShape.Loopback,
			body: new { pluginId, displayName = "Example Plugin" },
			extraHeaders: new Dictionary<string, string>
				{ [PluginAuthDefaults.EnrollmentTokenHeaderName] = enrollmentToken });

	private async Task<string> EnrollAndReadSecretAsync(string pluginId, string enrollmentToken)
	{
		var response = await EnrollAsync(pluginId, enrollmentToken);
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));

		return (await ReadJson(response)).GetProperty("pluginSecret").GetString()!;
	}

	private async Task<(string PluginId, string Secret)> PairPluginAsync()
	{
		var pluginId = NewPluginId();
		var verifier = $"verifier-{Guid.NewGuid():N}-{Guid.NewGuid():N}";
		var digest = SHA256.HashData(Encoding.UTF8.GetBytes(verifier));
		var challenge = Convert.ToBase64String(digest).TrimEnd('=').Replace('+', '-').Replace('/', '_');

		var created = await Send(HttpMethod.Post,
			"/api/plugins/pairing",
			shape: FakeConnectionShape.Loopback,
			body: new
			{
				pluginId, displayName = "Paired Plugin", codeChallenge = challenge, codeChallengeMethod = "S256"
			});
		Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created));
		var requestId = (await ReadJson(created)).GetProperty("requestId").GetString()!;

		var approve = await Send(HttpMethod.Post,
			$"/api/plugin-pairing/requests/{requestId}/approve",
			shape: FakeConnectionShape.Loopback,
			body: new { replaceExistingRegistration = false });
		Assert.That(approve.StatusCode, Is.EqualTo(HttpStatusCode.OK));

		var redemption = await Send(HttpMethod.Post,
			$"/api/plugins/pairing/{requestId}/redemption",
			shape: FakeConnectionShape.Loopback,
			body: new { codeVerifier = verifier });
		Assert.That(redemption.StatusCode, Is.EqualTo(HttpStatusCode.Created));

		return (pluginId, (await ReadJson(redemption)).GetProperty("pluginSecret").GetString()!);
	}

	private Task<HttpResponseMessage> CreateSessionAsync(string pluginId, string secret)
		=> Send(HttpMethod.Post,
			"/api/plugins/sessions",
			shape: FakeConnectionShape.Loopback,
			body: new
			{
				requestedVersion = new { minimum = 1, maximum = 1 }, capabilities = Array.Empty<object>()
			},
			extraHeaders: new Dictionary<string, string>
			{
				[PluginAuthDefaults.PluginIdHeaderName] = pluginId, [PluginAuthDefaults.PluginSecretHeaderName] = secret
			});

	private async Task<WebSocket> ConnectAsync(string pluginId, string secret)
	{
		var response = await CreateSessionAsync(pluginId, secret);
		Assert.That(response.StatusCode,
			Is.EqualTo(HttpStatusCode.Created),
			await response.Content.ReadAsStringAsync());
		var json = await ReadJson(response);
		var sessionId = json.GetProperty("sessionId").GetString()!;
		var sessionToken = json.GetProperty("sessionToken").GetString()!;
		var negotiatedVersion = json.GetProperty("negotiatedVersion").GetInt32();

		var client = _host.GetTestServer().CreateWebSocketClient();
		client.SubProtocols.Add(ProtocolConstants.WebSocketSubProtocol);
		client.ConfigureRequest = request =>
		{
			request.Headers[FakeConnectionStartupFilter.ShapeHeader] = nameof(FakeConnectionShape.Loopback);
			request.Headers["Authorization"] = $"Bearer {sessionToken}";
		};

		var socket = await client.ConnectAsync(new Uri($"ws://localhost{ProtocolConstants.WebSocketPath}"),
			CancellationToken.None);

		var hello = new ProtocolEnvelope
		{
			Type = MessageTypes.SessionHello,
			Id = Guid.CreateVersion7().ToString(),
			Payload = JsonSerializer.SerializeToElement(
				new SessionHelloPayload { ProtocolVersion = negotiatedVersion, SessionId = sessionId },
				PluginProtocolJson.Options)
		};
		await socket.SendAsync(ProtocolEnvelopeWriter.WriteToUtf8Bytes(hello),
			WebSocketMessageType.Text,
			true,
			CancellationToken.None);

		Assert.That((await ReceiveEnvelopeAsync(socket))?.Type, Is.EqualTo(MessageTypes.SessionWelcome));

		return socket;
	}

	private static async Task<ProtocolEnvelope?> ReceiveEnvelopeAsync(WebSocket socket)
	{
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		var buffer = new byte[32 * 1024];
		using var accumulated = new MemoryStream();

		while (true)
		{
			var result = await socket.ReceiveAsync(buffer, cts.Token);
			if (result.MessageType == WebSocketMessageType.Close)
			{
				return null;
			}

			await accumulated.WriteAsync(buffer.AsMemory(0, result.Count), cts.Token);
			if (result.EndOfMessage)
			{
				break;
			}
		}

		return ProtocolEnvelopeReader.Read(accumulated.ToArray()).Envelope;
	}

	private static StartupReadiness CompletedStartupReadiness()
	{
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();
		return readiness;
	}

	private async Task<HttpResponseMessage> Send(
		HttpMethod method,
		string path,
		FakeConnectionShape shape,
		string? bearerToken = null,
		object? body = null,
		IDictionary<string, string>? extraHeaders = null)
	{
		using var request = new HttpRequestMessage(method, path);
		if (body is not null)
		{
			request.Content = JsonContent.Create(body);
		}

		request.Headers.Add(FakeConnectionStartupFilter.ShapeHeader, shape.ToString());

		if (bearerToken is not null)
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
		}

		if (extraHeaders is not null)
		{
			foreach (var (key, value) in extraHeaders)
			{
				request.Headers.TryAddWithoutValidation(key, value);
			}
		}

		return await _client.SendAsync(request);
	}

	private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
		=> JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
