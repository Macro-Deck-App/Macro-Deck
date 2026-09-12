using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using MacroDeck.Plugin.Protocol;
using MacroDeck.Plugin.Protocol.Auth;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeck.Plugin.Protocol.Capabilities.Variables;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeck.Sdk;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[NonParallelizable]
public class PluginEndpointSecurityTests
{
	private IHost _host = null!;
	private HttpClient _client = null!;
	private string _dataDir = null!;
	private string? _previousDataDir;
	private string _adminToken = null!;
	private readonly FakePluginSupervisor _supervisor = new();

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
					services.RemoveAll<IPluginSupervisor>();
					services.AddSingleton<IPluginSupervisor>(_supervisor);
				});
			})
			.StartAsync();

		_client = _host.GetTestClient();

		var setup = await SendJson(HttpMethod.Post,
			"/api/auth/setup",
			new { username = "admin", password = "password123" },
			shape: FakeConnectionShape.Loopback);
		Assert.That(setup.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

		var login = await SendJson(HttpMethod.Post,
			"/api/auth/login",
			new { username = "admin", password = "password123", scope = "admin" });
		Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		_adminToken = (await ReadJson(login)).GetProperty("accessToken").GetString()!;

		// Enrolment and session creation for a self-registering plugin are gated on Developer Mode
		// (issue #753), and this fixture's plugins enrol; the one test that needs the switch off turns
		// it off itself.
		await SetDeveloperMode(true);
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

	private static readonly (HttpMethod Method, string Path)[] _pluginRoutes =
	[
		(HttpMethod.Get, "/api/plugins/protocol"),
		(HttpMethod.Post, "/api/plugins/registration"),
		(HttpMethod.Delete, "/api/plugins/registration/com.example.whatever"),
		(HttpMethod.Post, "/api/plugins/sessions"),
		(HttpMethod.Delete, "/api/plugins/sessions/whatever"),
		(HttpMethod.Post, "/api/plugins/pairing"),
		(HttpMethod.Get, "/api/plugins/pairing/whatever"),
		(HttpMethod.Post, "/api/plugins/pairing/whatever/redemption")
	];

	[Test]
	public async Task Every_plugin_path_refuses_a_non_loopback_remote_on_both_ports()
	{
		Assert.Multiple(async () =>
		{
			foreach (var (method, path) in _pluginRoutes)
			{
				var onPublicPort = await Send(method, path, shape: FakeConnectionShape.PublicLan);
				Assert.That(onPublicPort.StatusCode,
					Is.EqualTo(HttpStatusCode.Forbidden),
					$"{method} {path} on the public port with a LAN remote");

				var onPrivatePort = await Send(method, path, shape: FakeConnectionShape.LanOnPrivatePort);
				Assert.That(onPrivatePort.StatusCode,
					Is.EqualTo(HttpStatusCode.Forbidden),
					$"{method} {path} on the private listener with a LAN remote");
			}
		});
	}

	[Test]
	public async Task A_loopback_remote_on_the_public_port_succeeds()
	{
		var response = await Send(HttpMethod.Get,
			"/api/plugins/protocol",
			shape: FakeConnectionShape.LoopbackOnPublicPort);

		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
	}

	[Test]
	public async Task Get_protocol_descriptor_is_anonymous()
	{
		var response = await Send(HttpMethod.Get, "/api/plugins/protocol", shape: FakeConnectionShape.Loopback);
		var body = await ReadJson(response);

		Assert.Multiple(() =>
		{
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(body.GetProperty("supportedVersions").GetArrayLength(), Is.GreaterThan(0));
		});
	}

	[Test]
	public async Task A_cross_site_origin_is_refused_and_plugin_paths_carry_no_cors_headers()
	{
		var pluginResponse = await Send(HttpMethod.Get,
			"/api/plugins/protocol",
			shape: FakeConnectionShape.Loopback,
			extraHeaders: new Dictionary<string, string> { ["Origin"] = "https://evil.example" });

		// A normal (non-plugin) admin route proves the permissive CORS policy is otherwise in force -
		// the plugin path's absence of the header is a deliberate exclusion, not an accident of the
		// test fixture.
		var ordinaryResponse = await Send(HttpMethod.Get,
			"/api/system/version",
			shape: FakeConnectionShape.Loopback,
			extraHeaders: new Dictionary<string, string> { ["Origin"] = "https://evil.example" });

		Assert.Multiple(() =>
		{
			Assert.That(pluginResponse.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(pluginResponse.Headers.Contains("Access-Control-Allow-Origin"), Is.False);
			Assert.That(ordinaryResponse.Headers.Contains("Access-Control-Allow-Origin"), Is.True);
		});
	}

	[Test]
	public async Task Plugin_pairing_desktop_paths_carry_no_cors_headers_either()
	{
		// api/plugin-pairing is not on the plugin protocol path prefix ProtocolConstants.All covers, so
		// this is a separate exclusion clause in Startup.cs - the sole defence against a website the
		// developer happens to have open reading pending pairing requests (or approving one) via a
		// cross-origin fetch, since the trusted-loopback check below requires no credentials a browser
		// could be tricked into attaching.
		var pairingResponse = await Send(HttpMethod.Get,
			"/api/plugin-pairing/requests",
			shape: FakeConnectionShape.Loopback,
			bearerToken: _adminToken,
			extraHeaders: new Dictionary<string, string> { ["Origin"] = "https://evil.example" });

		var ordinaryResponse = await Send(HttpMethod.Get,
			"/api/system/version",
			shape: FakeConnectionShape.Loopback,
			extraHeaders: new Dictionary<string, string> { ["Origin"] = "https://evil.example" });

		Assert.Multiple(() =>
		{
			Assert.That(pairingResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(pairingResponse.Headers.Contains("Access-Control-Allow-Origin"), Is.False);
			Assert.That(ordinaryResponse.Headers.Contains("Access-Control-Allow-Origin"), Is.True);
		});
	}

	[Test]
	public async Task Unknown_plugin_id_and_wrong_secret_produce_byte_identical_401_responses()
	{
		var (registeredId, _) = await EnrollPluginAsync();

		var unknown = await CreateSessionAsync($"com.example.unknown{Guid.NewGuid():N}", "whatever-secret");
		var wrongSecret = await CreateSessionAsync(registeredId, "definitely-the-wrong-secret");

		var unknownBody = await unknown.Content.ReadAsStringAsync();
		var wrongSecretBody = await wrongSecret.Content.ReadAsStringAsync();

		Assert.Multiple(() =>
		{
			Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(wrongSecret.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(unknownBody, Is.EqualTo(wrongSecretBody));
			Assert.That(unknown.Content.Headers.ContentType?.MediaType,
				Is.EqualTo(wrongSecret.Content.Headers.ContentType?.MediaType));
		});
	}

	[Test]
	public async Task Every_authentication_failure_cause_produces_an_indistinguishable_401()
	{
		var (registeredId, registeredSecret) = await EnrollPluginAsync();

		var launchTokenService = _host.Services.GetRequiredService<IPluginLaunchTokenService>();
		var managedId = $"com.example.spent{Guid.NewGuid():N}";
		var spentSecret = launchTokenService.Mint(managedId, $"launch-{Guid.NewGuid():N}", "Spent Plugin", "1.0.0");
		Assert.That((await CreateSessionAsync(managedId, spentSecret)).StatusCode,
			Is.EqualTo(HttpStatusCode.Created));

		var mismatchedSecret = launchTokenService.Mint($"com.example.other{Guid.NewGuid():N}",
			$"launch-{Guid.NewGuid():N}",
			"Other Plugin",
			"1.0.0");

		var (revokedId, revokedSecret) = await EnrollPluginAsync();
		var revoke = await Send(HttpMethod.Delete,
			$"/api/plugins/registration/{revokedId}",
			shape: FakeConnectionShape.Loopback,
			bearerToken: _adminToken);
		Assert.That(revoke.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

		var (collidingId, collidingSecret) = await EnrollPluginAsync();
		var collidingLaunchSecret = launchTokenService.Mint(collidingId,
			$"launch-{Guid.NewGuid():N}",
			"Live Managed Plugin",
			"1.0.0");
		Assert.That((await CreateSessionAsync(collidingId, collidingLaunchSecret)).StatusCode,
			Is.EqualTo(HttpStatusCode.Created));

		var causes = new (string Cause, string PluginId, string Secret)[]
		{
			("unknown plugin id", $"com.example.unknown{Guid.NewGuid():N}", "whatever-secret"),
			("wrong secret", registeredId, "definitely-the-wrong-secret"),
			("empty secret", registeredId, string.Empty),
			("spent bootstrap token", managedId, spentSecret),
			("bootstrap token for another plugin", managedId, mismatchedSecret),
			("revoked registration", revokedId, revokedSecret),
			("registration secret while a managed launch is live", collidingId, collidingSecret)
		};

		var responses = new List<(string Cause, HttpStatusCode Status, string Body, string Headers)>();
		foreach (var (cause, pluginId, secret) in causes)
		{
			var response = await CreateSessionAsync(pluginId, secret);
			responses.Add((cause, response.StatusCode, await response.Content.ReadAsStringAsync(),
				DescribeHeaders(response)));
		}

		var reference = responses[0];
		Assert.Multiple(() =>
		{
			foreach (var candidate in responses)
			{
				Assert.That(candidate.Status, Is.EqualTo(HttpStatusCode.Unauthorized), candidate.Cause);
				Assert.That(candidate.Body, Is.EqualTo(reference.Body), candidate.Cause);
				Assert.That(candidate.Headers, Is.EqualTo(reference.Headers), candidate.Cause);
			}
		});
	}

	[Test]
	public async Task A_developer_registration_cannot_displace_a_running_managed_plugins_session()
	{
		var launchTokenService = _host.Services.GetRequiredService<IPluginLaunchTokenService>();
		var sessionRegistry = _host.Services.GetRequiredService<IPluginSessionRegistry>();
		var (pluginId, secret) = await EnrollPluginAsync();

		var launchSecret = launchTokenService.Mint(pluginId,
			$"launch-{Guid.NewGuid():N}",
			"Live Managed Plugin",
			"1.0.0");
		var managedSession = await CreateSessionAsync(pluginId, launchSecret);
		Assert.That(managedSession.StatusCode, Is.EqualTo(HttpStatusCode.Created));
		var managedSessionId = (await ReadJson(managedSession)).GetProperty("sessionId").GetString()!;

		var attempt = await CreateSessionAsync(pluginId, secret);
		Assert.That(attempt.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

		var currentSessionId = sessionRegistry.Snapshot()
			.Single(s => string.Equals(s.PluginId, pluginId, StringComparison.Ordinal)).SessionId;

		Assert.That(currentSessionId, Is.EqualTo(managedSessionId));
	}

	/// <summary>Issue #753: Developer Mode gates plugins that registered themselves. A plugin the user
	/// installed and the host launched is not developer tooling, and switching the setting off must
	/// leave it able to connect and be controlled.</summary>
	[Test]
	public async Task An_installed_plugin_still_connects_and_is_controllable_while_developer_mode_is_off()
	{
		await SetDeveloperMode(false);
		try
		{
			var pluginId = $"com.example.installed{Guid.NewGuid():N}";
			var launchSecret = _host.Services.GetRequiredService<IPluginLaunchTokenService>()
				.Mint(pluginId, $"launch-{Guid.NewGuid():N}", "Installed Plugin", "1.0.0");

			var session = await CreateSessionAsync(pluginId, launchSecret);
			var listed = (await ReadJson(await Send(HttpMethod.Get, "/api/plugin-sessions", _adminToken)))
				.GetProperty("sessions")
				.EnumerateArray()
				.Single(entry => entry.GetProperty("pluginId").GetString() == pluginId);

			var stop = await ReadJson(await Send(HttpMethod.Post, $"/api/plugin-runtime/{pluginId}/stop", _adminToken));
			var start
				= await ReadJson(await Send(HttpMethod.Post, $"/api/plugin-runtime/{pluginId}/start", _adminToken));

			Assert.Multiple(() =>
			{
				Assert.That(session.StatusCode, Is.EqualTo(HttpStatusCode.Created));
				Assert.That(listed.GetProperty("origin").GetString(), Is.EqualTo("managed"));
				Assert.That(stop.GetProperty("success").GetBoolean(), Is.True);
				Assert.That(start.GetProperty("success").GetBoolean(), Is.True);
			});
		}
		finally
		{
			await SetDeveloperMode(true);
		}
	}

	[Test]
	public async Task An_enrollment_secret_still_opens_a_session_when_no_launch_is_live()
	{
		var (pluginId, secret) = await EnrollPluginAsync();

		var response = await CreateSessionAsync(pluginId, secret);

		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
	}

	[Test]
	public async Task An_unknown_and_a_missing_enrollment_token_are_indistinguishable()
	{
		var missing = await Send(HttpMethod.Post,
			"/api/plugins/registration",
			shape: FakeConnectionShape.Loopback,
			body: new { pluginId = $"com.example.n{Guid.NewGuid():N}", displayName = "No token" });

		var unknown = await Send(HttpMethod.Post,
			"/api/plugins/registration",
			shape: FakeConnectionShape.Loopback,
			body: new { pluginId = $"com.example.n{Guid.NewGuid():N}", displayName = "Bad token" },
			extraHeaders: new Dictionary<string, string>
			{
				[PluginAuthDefaults.EnrollmentTokenHeaderName] = "not-a-real-token"
			});

		Assert.Multiple(async () =>
		{
			Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(await missing.Content.ReadAsStringAsync(),
				Is.EqualTo(await unknown.Content.ReadAsStringAsync()));
			Assert.That(DescribeHeaders(missing), Is.EqualTo(DescribeHeaders(unknown)));
		});
	}

	[Test]
	public async Task Enrollment_token_guessing_is_throttled_even_when_the_plugin_id_varies()
	{
		var throttle = _host.Services.GetRequiredKeyedService<LoginThrottle>("plugin");

		try
		{
			HttpResponseMessage? throttled = null;

			for (var attempt = 0; attempt < 10 && throttled is null; attempt++)
			{
				var response = await Send(HttpMethod.Post,
					"/api/plugins/registration",
					shape: FakeConnectionShape.Loopback,
					body: new { pluginId = $"com.example.g{Guid.NewGuid():N}", displayName = "Guess" },
					extraHeaders: new Dictionary<string, string>
					{
						[PluginAuthDefaults.EnrollmentTokenHeaderName] = $"guess-{Guid.NewGuid():N}"
					});

				if (response.StatusCode == HttpStatusCode.TooManyRequests)
				{
					throttled = response;
				}
			}

			Assert.That(throttled,
				Is.Not.Null,
				"A distinct plugin id per attempt must not buy an unthrottled enrollment-token guess.");
			Assert.That(throttled!.Headers.RetryAfter, Is.Not.Null);
		}
		finally
		{
			throttle.RegisterSuccess("plugin-enroll");
		}
	}

	[Test]
	public async Task A_managed_plugin_can_reconnect_after_its_session_is_terminated_for_it()
	{
		var launchTokenService = _host.Services.GetRequiredService<IPluginLaunchTokenService>();
		var pluginId = $"com.example.crash{Guid.NewGuid():N}";
		var launchSecret = launchTokenService.Mint(pluginId, $"launch-{Guid.NewGuid():N}", "Crash Plugin", "1.0.0");

		var first = await CreateSessionAsync(pluginId, launchSecret);
		Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.Created));
		var sessionId = (await ReadJson(first)).GetProperty("sessionId").GetString()!;

		var terminate = await Send(HttpMethod.Post,
			$"/api/plugin-sessions/{sessionId}/terminate",
			bearerToken: _adminToken);
		Assert.That(terminate.StatusCode, Is.EqualTo(HttpStatusCode.OK));

		var second = await CreateSessionAsync(pluginId, launchSecret);
		Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Created));
	}

	[Test]
	public async Task A_managed_plugins_session_is_named_by_the_manifest_name_the_host_read_at_launch()
	{
		var launchTokenService = _host.Services.GetRequiredService<IPluginLaunchTokenService>();
		var sessionRegistry = _host.Services.GetRequiredService<IPluginSessionRegistry>();
		var pluginId = $"com.example.named{Guid.NewGuid():N}";
		var launchSecret = launchTokenService.Mint(pluginId, $"launch-{Guid.NewGuid():N}", "Weather Widget", "1.0.0");

		var created = await CreateSessionAsync(pluginId, launchSecret);
		Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created));
		var sessionId = (await ReadJson(created)).GetProperty("sessionId").GetString()!;

		var session = sessionRegistry.Snapshot()
			.Single(s => string.Equals(s.SessionId, sessionId, StringComparison.Ordinal));

		Assert.Multiple(() =>
		{
			Assert.That(session.DisplayName, Is.EqualTo("Weather Widget"));
			Assert.That(session.DisplayName, Is.Not.EqualTo(pluginId));
		});
	}

	[Test]
	public async Task An_over_long_declared_name_is_refused_the_same_way_an_over_long_declared_version_is()
	{
		var (pluginId, secret) = await EnrollPluginAsync();

		var nameResponse = await CreateSessionAsync(pluginId, secret, declaredName: new string('9', 129));
		var (otherPluginId, otherSecret) = await EnrollPluginAsync();
		var versionResponse =
			await CreateSessionAsync(otherPluginId, otherSecret, declaredVersion: new string('9', 129));

		var nameJson = await ReadJson(nameResponse);
		var versionJson = await ReadJson(versionResponse);

		Assert.Multiple(() =>
		{
			Assert.That(nameResponse.StatusCode, Is.EqualTo(versionResponse.StatusCode));
			Assert.That(nameJson.GetProperty("code").GetString(),
				Is.EqualTo(versionJson.GetProperty("code").GetString()));
		});
	}

	[Test]
	public async Task A_managed_plugin_cannot_rename_itself_by_declaring_a_name()
	{
		var launchTokenService = _host.Services.GetRequiredService<IPluginLaunchTokenService>();
		var sessionRegistry = _host.Services.GetRequiredService<IPluginSessionRegistry>();
		var pluginId = $"com.example.norename{Guid.NewGuid():N}";
		var launchSecret = launchTokenService.Mint(pluginId, $"launch-{Guid.NewGuid():N}", "Weather Widget", "1.0.0");

		var created = await CreateSessionAsync(pluginId, launchSecret, declaredName: "Totally Legit Bank");
		Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created));
		var sessionId = (await ReadJson(created)).GetProperty("sessionId").GetString()!;

		var session = sessionRegistry.Snapshot()
			.Single(s => string.Equals(s.SessionId, sessionId, StringComparison.Ordinal));

		Assert.That(session.DisplayName, Is.EqualTo("Weather Widget"));
	}

	[Test]
	public async Task A_declared_name_does_not_persist_into_the_enrollment()
	{
		var createToken = await SendJson(HttpMethod.Post,
			"/api/plugin-tokens",
			new { name = $"test-{Guid.NewGuid():N}", expiresInDays = (int?)null },
			bearerToken: _adminToken);
		var tokenJson = await ReadJson(createToken);
		var enrollmentToken = tokenJson.GetProperty("plaintext").GetString()!;

		var pluginId = $"com.example.t{Guid.NewGuid():N}";
		var register = await Send(HttpMethod.Post,
			"/api/plugins/registration",
			shape: FakeConnectionShape.Loopback,
			body: new { pluginId, displayName = "Enrolled Label" },
			extraHeaders: new Dictionary<string, string>
				{ [PluginAuthDefaults.EnrollmentTokenHeaderName] = enrollmentToken });
		var registerJson = await ReadJson(register);
		var secret = registerJson.GetProperty("pluginSecret").GetString()!;

		var sessionRegistry = _host.Services.GetRequiredService<IPluginSessionRegistry>();

		var first = await CreateSessionAsync(pluginId, secret, declaredName: "Renamed");
		Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.Created));
		var firstJson = await ReadJson(first);
		var firstSessionId = firstJson.GetProperty("sessionId").GetString()!;
		var closeFirst = await Send(HttpMethod.Delete,
			$"/api/plugins/sessions/{firstSessionId}",
			shape: FakeConnectionShape.Loopback,
			bearerToken: firstJson.GetProperty("sessionToken").GetString());
		Assert.That(closeFirst.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

		var second = await CreateSessionAsync(pluginId, secret);
		Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Created));
		var secondSessionId = (await ReadJson(second)).GetProperty("sessionId").GetString()!;

		var session = sessionRegistry.Snapshot()
			.Single(s => string.Equals(s.SessionId, secondSessionId, StringComparison.Ordinal));

		Assert.That(session.DisplayName, Is.EqualTo("Enrolled Label"));
	}

	[Test]
	public async Task Ws_rejects_no_token_and_an_ambient_admin_jwt()
	{
		var noToken = Assert.ThrowsAsync<InvalidOperationException>(async () =>
			await ConnectWebSocketAsync(_ => { }));
		Assert.That(noToken!.Message, Does.Contain("401"));

		// The ambient-admin trap: a valid admin JWT is not a plugin session token, and must not be
		// accepted just because it authenticates successfully against *some* scheme.
		var adminJwt = Assert.ThrowsAsync<InvalidOperationException>(async () =>
			await ConnectWebSocketAsync(request => request.Headers["Authorization"] = $"Bearer {_adminToken}"));
		Assert.That(adminJwt!.Message, Does.Contain("401"));
	}

	[Test]
	public async Task Ws_without_the_subprotocol_is_rejected_with_400()
	{
		var (pluginId, secret) = await EnrollPluginAsync();
		var session = await CreateSessionAndReadAsync(pluginId, secret);

		var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
		{
			var client = _host.GetTestServer().CreateWebSocketClient();
			// Deliberately no subprotocol.
			client.ConfigureRequest = request =>
			{
				request.Headers[FakeConnectionStartupFilter.ShapeHeader] = nameof(FakeConnectionShape.Loopback);
				request.Headers["Authorization"] = $"Bearer {session.SessionToken}";
			};
			await client.ConnectAsync(WsUri(), CancellationToken.None);
		});

		Assert.That(exception!.Message, Does.Contain("400"));
	}

	[Test]
	public async Task A_managed_plugins_bootstrap_token_is_single_use_and_releases_on_session_end()
	{
		var launchTokenService = _host.Services.GetRequiredService<IPluginLaunchTokenService>();
		var pluginId = $"com.example.managed{Guid.NewGuid():N}";
		var launchSecret = launchTokenService.Mint(pluginId, "launch-1", "Managed Plugin", "1.0.0");

		var first = await CreateSessionAsync(pluginId, launchSecret);
		Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.Created));
		var firstSession = await ReadJson(first);
		var sessionToken = firstSession.GetProperty("sessionToken").GetString()!;
		var sessionId = firstSession.GetProperty("sessionId").GetString()!;

		var second = await CreateSessionAsync(pluginId, launchSecret);
		Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

		var close = await Send(HttpMethod.Delete,
			$"/api/plugins/sessions/{sessionId}",
			shape: FakeConnectionShape.Loopback,
			bearerToken: sessionToken);
		Assert.That(close.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

		var third = await CreateSessionAsync(pluginId, launchSecret);
		Assert.That(third.StatusCode, Is.EqualTo(HttpStatusCode.Created));
	}

	[Test]
	public async Task End_to_end_token_revocation_terminates_the_session_and_blocks_re_authentication()
	{
		var tokenName = $"e2e-{Guid.NewGuid():N}";
		var createToken = await SendJson(HttpMethod.Post,
			"/api/plugin-tokens",
			new { name = tokenName, expiresInDays = (int?)null },
			bearerToken: _adminToken);
		Assert.That(createToken.StatusCode, Is.EqualTo(HttpStatusCode.Created));
		var tokenJson = await ReadJson(createToken);
		var enrollmentToken = tokenJson.GetProperty("plaintext").GetString()!;
		var tokenId = tokenJson.GetProperty("token").GetProperty("id").GetString()!;

		var pluginId = $"com.example.e2e{Guid.NewGuid():N}";
		var register = await Send(HttpMethod.Post,
			"/api/plugins/registration",
			shape: FakeConnectionShape.Loopback,
			body: new { pluginId, displayName = "E2E Plugin" },
			extraHeaders: new Dictionary<string, string>
			{
				[PluginAuthDefaults.EnrollmentTokenHeaderName] = enrollmentToken
			});
		Assert.That(register.StatusCode, Is.EqualTo(HttpStatusCode.Created));
		var registerJson = await ReadJson(register);
		var secret = registerJson.GetProperty("pluginSecret").GetString()!;

		var session = await CreateSessionAndReadAsync(pluginId, secret);

		using var socket = await ConnectWebSocketAsync(request
			=> request.Headers["Authorization"] = $"Bearer {session.SessionToken}");

		await SendEnvelopeAsync(socket,
			new ProtocolEnvelope
			{
				Type = MessageTypes.SessionHello,
				Id = Guid.CreateVersion7().ToString(),
				Payload = JsonSerializer.SerializeToElement(new SessionHelloPayload
						{ ProtocolVersion = session.NegotiatedVersion, SessionId = session.SessionId },
					PluginProtocolJson.Options)
			});

		var welcome = await ReceiveEnvelopeAsync(socket);
		Assert.That(welcome?.Type, Is.EqualTo(MessageTypes.SessionWelcome));

		var revoke = await Send(HttpMethod.Post, $"/api/plugin-tokens/{tokenId}/revoke", bearerToken: _adminToken);
		Assert.That(revoke.StatusCode, Is.EqualTo(HttpStatusCode.OK));

		var afterRevoke = await ReceiveNonStateEnvelopeOrCloseAsync(socket);
		Assert.That(afterRevoke, Is.Null, "the socket should be closed after the token was revoked");

		var reAuthenticate = await CreateSessionAsync(pluginId, secret);
		Assert.That(reAuthenticate.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
	}

	[Test]
	public async Task After_goodbye_the_plugin_is_unreachable_and_its_delete_still_succeeds()
	{
		var (pluginId, secret) = await EnrollPluginAsync();
		var session = await CreateSessionAndReadAsync(pluginId, secret);
		var sessionRegistry = _host.Services.GetRequiredService<IPluginSessionRegistry>();

		using var socket = await ConnectWebSocketAsync(request
			=> request.Headers["Authorization"] = $"Bearer {session.SessionToken}");
		await SendEnvelopeAsync(socket,
			new ProtocolEnvelope
			{
				Type = MessageTypes.SessionHello,
				Id = Guid.CreateVersion7().ToString(),
				Payload = JsonSerializer.SerializeToElement(new SessionHelloPayload
						{ ProtocolVersion = session.NegotiatedVersion, SessionId = session.SessionId },
					PluginProtocolJson.Options)
			});
		Assert.That((await ReceiveEnvelopeAsync(socket))?.Type, Is.EqualTo(MessageTypes.SessionWelcome));

		await SendEnvelopeAsync(socket,
			new ProtocolEnvelope
			{
				Type = MessageTypes.SessionGoodbye,
				Id = Guid.CreateVersion7().ToString(),
				Payload = JsonSerializer.SerializeToElement(new { reason = "shutting down" })
			});
		Assert.That(await ReceiveNonStateEnvelopeOrCloseAsync(socket), Is.Null, "the host closes after goodbye");
		await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);

		var deadline = DateTime.UtcNow.AddSeconds(10);
		while (State() == PluginSessionState.Connected && DateTime.UtcNow < deadline)
		{
			await Task.Delay(20);
		}

		Assert.That(State(), Is.EqualTo(PluginSessionState.Dropped));

		var sent = await sessionRegistry.SendToPlugin(pluginId,
			new ProtocolEnvelope { Type = MessageTypes.SessionPing, Id = Guid.CreateVersion7().ToString() });
		var delete = await Send(HttpMethod.Delete,
			$"/api/plugins/sessions/{session.SessionId}",
			FakeConnectionShape.Loopback,
			bearerToken: session.SessionToken);

		Assert.Multiple(() =>
		{
			Assert.That(sent, Is.False);
			Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
		});

		PluginSessionState? State() => sessionRegistry.Snapshot()
			.FirstOrDefault(s => s.SessionId == session.SessionId)?.State;
	}

	[Test]
	public async Task Ws_capability_result_for_an_unrecognised_correlation_is_reported_as_correlation_unknown()
	{
		var (pluginId, secret) = await EnrollPluginAsync();
		var session = await CreateSessionAndReadAsync(pluginId, secret);

		using var socket = await ConnectWebSocketAsync(request
			=> request.Headers["Authorization"] = $"Bearer {session.SessionToken}");

		await SendEnvelopeAsync(socket,
			new ProtocolEnvelope
			{
				Type = MessageTypes.SessionHello,
				Id = Guid.CreateVersion7().ToString(),
				Payload = JsonSerializer.SerializeToElement(new SessionHelloPayload
						{ ProtocolVersion = session.NegotiatedVersion, SessionId = session.SessionId },
					PluginProtocolJson.Options)
			});

		var welcome = await ReceiveEnvelopeAsync(socket);
		Assert.That(welcome?.Type, Is.EqualTo(MessageTypes.SessionWelcome));

		await SendEnvelopeAsync(socket,
			new ProtocolEnvelope
			{
				Type = MessageTypes.CapabilityResult,
				Id = Guid.CreateVersion7().ToString(),
				CorrelationId = Guid.CreateVersion7().ToString()
			});

		var reply = await ReceiveNonStateEnvelopeAsync(socket);

		Assert.Multiple(() =>
		{
			Assert.That(reply?.Type, Is.EqualTo(MessageTypes.ProtocolError));
			Assert.That(reply?.Error?.Code, Is.EqualTo(ProtocolErrorCodes.CorrelationUnknown));
		});
	}

	[Test]
	public async Task Registration_runs_concurrently_with_the_message_loop_so_the_plugin_ends_up_registered()
	{
		var (pluginId, secret) = await EnrollPluginAsync();
		var session = await CreateSessionAndReadAsync(pluginId,
			secret,
			capabilities:
			[
				new
				{
					kind = CapabilityKinds.Actions, localId = "provider",
					versionRange = new { minimum = 1, maximum = 1 }
				},
				new
				{
					kind = CapabilityKinds.Variables, localId = "provider",
					versionRange = new { minimum = 1, maximum = 1 }
				}
			]);

		using var socket = await ConnectWebSocketAsync(request
			=> request.Headers["Authorization"] = $"Bearer {session.SessionToken}");

		await SendEnvelopeAsync(socket,
			new ProtocolEnvelope
			{
				Type = MessageTypes.SessionHello,
				Id = Guid.CreateVersion7().ToString(),
				Payload = JsonSerializer.SerializeToElement(new SessionHelloPayload
						{ ProtocolVersion = session.NegotiatedVersion, SessionId = session.SessionId },
					PluginProtocolJson.Options)
			});

		var welcome = await ReceiveEnvelopeAsync(socket);
		Assert.That(welcome?.Type, Is.EqualTo(MessageTypes.SessionWelcome));

		using var listenerCts = new CancellationTokenSource();
		var respondToDescribes = RespondToCapabilityInvokesAsync(socket, listenerCts.Token);

		var registry = _host.Services.GetRequiredService<IIntegrationRegistry>();

		// Comfortably shorter than ProtocolTimeouts.CapabilityInvoke (30s): a regression must fail this
		// assertion fast, not slow-timeout the suite.
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
		IIntegration? integration = null;
		while (DateTime.UtcNow < deadline)
		{
			integration = registry.Integrations.FirstOrDefault(candidate => candidate.Id == pluginId);
			if (integration is not null)
			{
				break;
			}

			await Task.Delay(50);
		}

		await listenerCts.CancelAsync();
		socket.Abort();
		await Quietly(respondToDescribes);

		Assert.That(integration,
			Is.Not.Null,
			"the plugin should be registered once its describes are answered - registration must run " +
			"concurrently with the message loop that reads those replies, not before it");
	}

	private static async Task RespondToCapabilityInvokesAsync(WebSocket socket, CancellationToken cancellationToken)
	{
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				var envelope = await ReceiveEnvelopeOrCloseAsync(socket);
				if (envelope is null)
				{
					return;
				}

				if (string.Equals(envelope.Type, MessageTypes.CapabilityInvoke, StringComparison.Ordinal))
				{
					await SendEnvelopeAsync(socket, BuildCapabilityResult(envelope));
				}
			}
		}
		catch (Exception exception) when (exception is OperationCanceledException
			or WebSocketException
			or ObjectDisposedException)
		{
		}
	}

	private static ProtocolEnvelope BuildCapabilityResult(ProtocolEnvelope invoke)
	{
		var payload = invoke.Payload!.Value.Deserialize<CapabilityInvokePayload>(PluginProtocolJson.Options)!;

		var data = payload.Kind switch
		{
			CapabilityKinds.Actions => JsonSerializer.SerializeToElement(new ActionCatalogPayload { Actions = [] },
				PluginProtocolJson.Options),
			CapabilityKinds.Variables => JsonSerializer.SerializeToElement(
				new VariableCatalogPayload { DeclaredVariables = [], Variables = [] },
				PluginProtocolJson.Options),
			_ => JsonSerializer.SerializeToElement(new { }, PluginProtocolJson.Options)
		};

		return new ProtocolEnvelope
		{
			Type = MessageTypes.CapabilityResult,
			Id = Guid.CreateVersion7().ToString(),
			CorrelationId = invoke.Id,
			Payload = JsonSerializer.SerializeToElement(new CapabilityResultPayload { Data = data },
				PluginProtocolJson.Options)
		};
	}

	private static async Task Quietly(Task task)
	{
		try
		{
			await task;
		}
		catch (Exception exception) when (exception is OperationCanceledException
			or WebSocketException
			or ObjectDisposedException)
		{
		}
	}


	private async Task SetDeveloperMode(bool enabled)
	{
		var response = await SendJson(HttpMethod.Put,
			"/api/settings/developer",
			new { enabled },
			bearerToken: _adminToken);
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
	}

	private async Task<(string PluginId, string Secret)> EnrollPluginAsync()
	{
		var createToken = await SendJson(HttpMethod.Post,
			"/api/plugin-tokens",
			new { name = $"test-{Guid.NewGuid():N}", expiresInDays = (int?)null },
			bearerToken: _adminToken);
		var tokenJson = await ReadJson(createToken);
		var enrollmentToken = tokenJson.GetProperty("plaintext").GetString()!;

		var pluginId = $"com.example.t{Guid.NewGuid():N}";
		var register = await Send(HttpMethod.Post,
			"/api/plugins/registration",
			shape: FakeConnectionShape.Loopback,
			body: new { pluginId, displayName = "Test Plugin" },
			extraHeaders: new Dictionary<string, string>
				{ [PluginAuthDefaults.EnrollmentTokenHeaderName] = enrollmentToken });
		var registerJson = await ReadJson(register);

		return (pluginId, registerJson.GetProperty("pluginSecret").GetString()!);
	}

	private async Task<PluginSessionResponse> CreateSessionAndReadAsync(
		string pluginId,
		string secret,
		object[]? capabilities = null)
	{
		var response = await CreateSessionAsync(pluginId, secret, capabilities);
		Assert.That(response.StatusCode,
			Is.EqualTo(HttpStatusCode.Created),
			await response.Content.ReadAsStringAsync());
		var json = await ReadJson(response);

		return new PluginSessionResponse(json.GetProperty("sessionId").GetString()!,
			json.GetProperty("sessionToken").GetString()!,
			json.GetProperty("negotiatedVersion").GetInt32());
	}

	private sealed record PluginSessionResponse(string SessionId, string SessionToken, int NegotiatedVersion);

	private Task<HttpResponseMessage> CreateSessionAsync(string pluginId,
		string secret,
		object[]? capabilities = null,
		string? declaredName = null,
		string? declaredVersion = null)
	{
		var body = new Dictionary<string, object?>
		{
			["requestedVersion"] = new { minimum = 1, maximum = 1 },
			["capabilities"] = capabilities ?? Array.Empty<object>()
		};

		if (declaredName is not null)
		{
			body["declaredName"] = declaredName;
		}

		if (declaredVersion is not null)
		{
			body["declaredVersion"] = declaredVersion;
		}

		return Send(HttpMethod.Post,
			"/api/plugins/sessions",
			shape: FakeConnectionShape.Loopback,
			body: body,
			extraHeaders: new Dictionary<string, string>
			{
				[PluginAuthDefaults.PluginIdHeaderName] = pluginId, [PluginAuthDefaults.PluginSecretHeaderName] = secret
			});
	}

	private async Task<WebSocket> ConnectWebSocketAsync(Action<HttpRequest> configureRequest)
	{
		var client = _host.GetTestServer().CreateWebSocketClient();
		client.SubProtocols.Add(ProtocolConstants.WebSocketSubProtocol);
		client.ConfigureRequest = request =>
		{
			request.Headers[FakeConnectionStartupFilter.ShapeHeader] = nameof(FakeConnectionShape.Loopback);
			configureRequest(request);
		};

		return await client.ConnectAsync(WsUri(), CancellationToken.None);
	}

	private static Uri WsUri() => new($"ws://localhost{ProtocolConstants.WebSocketPath}");

	private static async Task SendEnvelopeAsync(WebSocket socket, ProtocolEnvelope envelope)
	{
		var bytes = ProtocolEnvelopeWriter.WriteToUtf8Bytes(envelope);
		await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
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

	private static async Task<ProtocolEnvelope?> ReceiveEnvelopeOrCloseAsync(WebSocket socket)
	{
		try
		{
			return await ReceiveEnvelopeAsync(socket);
		}
		catch (OperationCanceledException)
		{
			return null;
		}
		catch (WebSocketException)
		{
			return null;
		}
	}

	private static async Task<ProtocolEnvelope?> ReceiveNonStateEnvelopeAsync(WebSocket socket)
	{
		while (true)
		{
			var envelope = await ReceiveEnvelopeAsync(socket);
			if (envelope is null || !string.Equals(envelope.Type, MessageTypes.HostState, StringComparison.Ordinal))
			{
				return envelope;
			}
		}
	}

	private static async Task<ProtocolEnvelope?> ReceiveNonStateEnvelopeOrCloseAsync(WebSocket socket)
	{
		while (true)
		{
			var envelope = await ReceiveEnvelopeOrCloseAsync(socket);
			if (envelope is null || !string.Equals(envelope.Type, MessageTypes.HostState, StringComparison.Ordinal))
			{
				return envelope;
			}
		}
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

	private Task<HttpResponseMessage> Send(HttpMethod method, string path, string? bearerToken = null)
		=> Send(method, path, FakeConnectionShape.PublicLan, bearerToken);

	private Task<HttpResponseMessage> SendJson(
		HttpMethod method,
		string path,
		object body,
		FakeConnectionShape shape = FakeConnectionShape.PublicLan,
		string? bearerToken = null)
		=> Send(method, path, shape, bearerToken, body);

	private static string DescribeHeaders(HttpResponseMessage response)
		=> string.Join('\n',
			response.Headers.Concat(response.Content.Headers)
				.Select(header => $"{header.Key}: {string.Join(',', header.Value)}")
				.OrderBy(line => line, StringComparer.Ordinal));

	private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
		=> JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
