using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Ui;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Auth;

[NonParallelizable]
public class AuthPolicyMatrixTests
{
	private static readonly string[] UnsubscribeLabelArguments = ["widget-1", "default"];
	private IHost _host = null!;
	private HttpClient _client = null!;
	private string _dataDir = null!;
	private string? _previousDataDir;
	private string _clientToken = null!;
	private string _adminToken = null!;

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
				});
			})
			.StartAsync();

		_client = _host.GetTestClient();

		var setup = await SendJson(HttpMethod.Post,
			"/api/auth/setup",
			new { username = "admin", password = "password123" },
			loopback: true);
		Assert.That(setup.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

		_clientToken = await LoginAndGetToken("client");
		_adminToken = await LoginAndGetToken("admin");
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
	public async Task Anonymous_requests_are_rejected_on_protected_routes()
	{
		var folders = await Send(HttpMethod.Get, "/api/folders");
		var filesystem = await SendJson(HttpMethod.Post, "/api/filesystem/list", new { path = "" });
		var iconImage = await Send(HttpMethod.Get, $"/api/icons/{Guid.NewGuid()}/image");
		var devices = await Send(HttpMethod.Get, "/api/devices");
		var focusRules = await Send(HttpMethod.Get, "/api/folders/focus-rules");
		var applicationFocus = await Send(HttpMethod.Get, "/api/system/application-focus");
		var runningApplications = await Send(HttpMethod.Get, "/api/system/running-applications");
		var setStartupProfile = await SendJson(HttpMethod.Patch,
			$"/api/devices/{Guid.NewGuid()}/startup-profile",
			new { profileId = (string?)null });
		var openProfileOnDevice = await SendJson(HttpMethod.Post,
			$"/api/devices/{Guid.NewGuid()}/open-profile",
			new { profileId = "p1" });
		var logging = await Send(HttpMethod.Get, "/api/settings/logging");
		var pluginTokens = await Send(HttpMethod.Get, "/api/plugin-tokens");
		var pluginSessions = await Send(HttpMethod.Get, "/api/plugin-sessions");
		var localization = await Send(HttpMethod.Get, "/api/localization");

		Assert.Multiple(() =>
		{
			Assert.That(folders.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(filesystem.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(iconImage.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(devices.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(focusRules.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(applicationFocus.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(runningApplications.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(setStartupProfile.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(openProfileOnDevice.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(logging.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(pluginTokens.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(pluginSessions.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(localization.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
		});
	}

	[Test]
	public async Task Status_is_anonymous_and_reports_loopback_as_authenticated_admin()
	{
		var publicStatus = await ReadJson(await Send(HttpMethod.Get, "/api/auth/status"));
		var loopbackStatus = await ReadJson(await Send(HttpMethod.Get, "/api/auth/status", loopback: true));

		Assert.Multiple(() =>
		{
			Assert.That(publicStatus.GetProperty("setupComplete").GetBoolean(), Is.True);
			Assert.That(publicStatus.GetProperty("authenticated").GetBoolean(), Is.False);
			Assert.That(loopbackStatus.GetProperty("authenticated").GetBoolean(), Is.True);
			Assert.That(loopbackStatus.GetProperty("trusted").GetBoolean(), Is.True);
			Assert.That(loopbackStatus.GetProperty("scope").GetString(), Is.EqualTo("admin"));
		});
	}

	[Test]
	public async Task Setup_is_forbidden_on_the_public_port_and_conflicts_once_complete()
	{
		var publicSetup = await SendJson(HttpMethod.Post,
			"/api/auth/setup",
			new { username = "attacker", password = "password123" });
		var secondSetup = await SendJson(HttpMethod.Post,
			"/api/auth/setup",
			new { username = "second", password = "password123" },
			loopback: true);

		Assert.Multiple(() =>
		{
			Assert.That(publicSetup.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(secondSetup.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
		});
	}

	[Test]
	public async Task Device_provisioning_is_refused_off_the_loopback_listener_even_with_an_admin_token()
	{
		// Issue #727. These actions drive a device physically attached to this machine and write to its
		// filesystem, so the gate is the transport, not the scope: an admin token issued to a remote
		// client is still refused. Same reasoning as first-run setup (ADR 0003).
		var list = await Send(HttpMethod.Get, "/api/client-targets", _adminToken);
		var start = await SendJson(HttpMethod.Post,
			"/api/client-targets/carthing/provisioning/start",
			new { },
			_adminToken);
		var advance = await SendJson(HttpMethod.Post,
			"/api/client-targets/carthing/provisioning/advance",
			new { stepId = "detect", input = new Dictionary<string, string>() },
			_adminToken);
		var loopbackList = await Send(HttpMethod.Get, "/api/client-targets", loopback: true);

		Assert.Multiple(() =>
		{
			Assert.That(list.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(start.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(advance.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(loopbackList.StatusCode,
				Is.EqualTo(HttpStatusCode.OK),
				"the desktop app is the one caller that may do this");
		});
	}

	[Test]
	public async Task Minting_a_device_enrollment_is_refused_off_the_loopback_listener()
	{
		// Whoever asks for one is handing a device a session. That has to be someone sitting at this
		// machine, not a caller on the network - so an admin token is not enough either.
		var remote = await SendJson(HttpMethod.Post, "/api/auth/device-enrollment", new { }, _adminToken);
		var loopback = await SendJson(HttpMethod.Post, "/api/auth/device-enrollment", new { }, loopback: true);

		Assert.Multiple(() =>
		{
			Assert.That(remote.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(loopback.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		});
	}

	[Test]
	public async Task An_enrollment_credential_is_spent_by_its_first_use()
	{
		var minted = await SendJson(HttpMethod.Post, "/api/auth/device-enrollment", new { }, loopback: true);
		var token = (await ReadJson(minted)).GetProperty("token").GetString();

		var first = await SendJson(HttpMethod.Post, "/api/auth/device-enrollment/redeem", new { token });
		var replay = await SendJson(HttpMethod.Post, "/api/auth/device-enrollment/redeem", new { token });

		Assert.Multiple(() =>
		{
			Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(replay.StatusCode,
				Is.Not.EqualTo(HttpStatusCode.OK),
				"a credential that survived its first use could be replayed out of the file it sits in");
		});
	}

	[Test]
	public async Task An_enrolled_device_gets_client_scope_and_never_admin()
	{
		var minted = await SendJson(HttpMethod.Post, "/api/auth/device-enrollment", new { }, loopback: true);
		var token = (await ReadJson(minted)).GetProperty("token").GetString();

		var redeemed = await SendJson(HttpMethod.Post, "/api/auth/device-enrollment/redeem", new { token });
		var session = await ReadJson(redeemed);
		var accessToken = session.GetProperty("accessToken").GetString();

		var deckRead = await Send(HttpMethod.Get, "/api/folders", accessToken);
		var adminRead = await SendJson(HttpMethod.Post, "/api/filesystem/list", new { path = "" }, accessToken);

		Assert.Multiple(() =>
		{
			Assert.That(session.GetProperty("scope").GetString(), Is.EqualTo("client"));
			Assert.That(deckRead.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(adminRead.StatusCode,
				Is.EqualTo(HttpStatusCode.Forbidden),
				"enrollment exists to run a deck, never to hand a device the admin surface");
		});
	}

	[Test]
	public async Task Client_scope_can_read_the_lock_screen_setting_but_not_change_it()
	{
		var get = await Send(HttpMethod.Get, "/api/settings/lock-screen", _clientToken);
		var put = await SendJson(HttpMethod.Put, "/api/settings/lock-screen", new { enabled = true }, _clientToken);

		Assert.Multiple(() =>
		{
			Assert.That(get.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
		});
	}

	[Test]
	public async Task Admin_scope_can_update_the_lock_screen_setting_and_a_later_read_reflects_it()
	{
		var put = await SendJson(HttpMethod.Put, "/api/settings/lock-screen", new { enabled = true }, _adminToken);
		var get = await ReadJson(await Send(HttpMethod.Get, "/api/settings/lock-screen", _adminToken));

		Assert.Multiple(() =>
		{
			Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(get.GetProperty("enabled").GetBoolean(), Is.True);
		});

		// Leave the setting as found for other tests in this fixture.
		await SendJson(HttpMethod.Put, "/api/settings/lock-screen", new { enabled = false }, _adminToken);
	}

	[Test]
	public async Task The_onboarding_state_is_admin_only()
	{
		var get = await Send(HttpMethod.Get, "/api/settings/onboarding", _clientToken);
		var complete = await SendJson(HttpMethod.Post, "/api/settings/onboarding/complete", new { }, _clientToken);

		Assert.Multiple(() =>
		{
			Assert.That(get.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(complete.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
		});
	}

	[Test]
	public async Task Client_scope_reaches_viewer_routes_only()
	{
		var folders = await Send(HttpMethod.Get, "/api/folders", _clientToken);
		var variables = await Send(HttpMethod.Get, "/api/variables", _clientToken);
		var createVariable = await SendJson(HttpMethod.Post,
			"/api/variables",
			new { name = "test", scope = "global", type = "text" },
			_clientToken);
		var version = await Send(HttpMethod.Get, "/api/system/version", _clientToken);
		var serverTime = await Send(HttpMethod.Get, "/api/system/time", _clientToken);
		var integrationIcon = await Send(HttpMethod.Get, "/api/integrations/unknown/icon", _clientToken);
		var filesystem = await SendJson(HttpMethod.Post, "/api/filesystem/list", new { path = "" }, _clientToken);
		var reveal = await SendJson(HttpMethod.Post, $"/api/secrets/{Guid.NewGuid()}/reveal", new { }, _clientToken);
		var connectionInfo = await Send(HttpMethod.Get, "/api/system/connection-info", _clientToken);
		var updateAppearance = await SendJson(HttpMethod.Put,
			"/api/settings/appearance",
			new { themeMode = "dark" },
			_clientToken);
		var shutdown = await SendJson(HttpMethod.Post, "/api/host/shutdown", new { }, _clientToken);
		var shellNotifications = await Send(HttpMethod.Get, "/api/host/shell-notifications", _clientToken);
		var reportShellNotification = await SendJson(HttpMethod.Post,
			"/api/host/shell-notifications/1/result",
			new { shown = true },
			_clientToken);
		var devices = await Send(HttpMethod.Get, "/api/devices", _clientToken);
		var focusRules = await Send(HttpMethod.Get, "/api/folders/focus-rules", _clientToken);
		var applicationFocus = await Send(HttpMethod.Get, "/api/system/application-focus", _clientToken);
		var runningApplications = await Send(HttpMethod.Get, "/api/system/running-applications", _clientToken);
		var setFocusRule = await SendJson(HttpMethod.Put,
			$"/api/folders/{Guid.NewGuid()}/focus-rules",
			new
			{
				applicationIdentity = "notepad.exe", identityKind = "ExecutablePath",
				deviceId = Guid.NewGuid().ToString()
			},
			_clientToken);
		var deleteFocusRule = await Send(HttpMethod.Delete,
			$"/api/folders/{Guid.NewGuid()}/focus-rules/{Guid.NewGuid()}",
			_clientToken);
		var setStartupProfile = await SendJson(HttpMethod.Patch,
			$"/api/devices/{Guid.NewGuid()}/startup-profile",
			new { profileId = (string?)null },
			_clientToken);
		var openProfileOnDevice = await SendJson(HttpMethod.Post,
			$"/api/devices/{Guid.NewGuid()}/open-profile",
			new { profileId = "p1" },
			_clientToken);
		var logging = await Send(HttpMethod.Get, "/api/settings/logging", _clientToken);
		var pluginTokens = await Send(HttpMethod.Get, "/api/plugin-tokens", _clientToken);
		var pluginSessions = await Send(HttpMethod.Get, "/api/plugin-sessions", _clientToken);
		var localization = await Send(HttpMethod.Get, "/api/localization", _clientToken);
		var localizationSettings = await Send(HttpMethod.Get, "/api/settings/localization", _clientToken);
		var updateLocalizationSettings = await SendJson(HttpMethod.Put,
			"/api/settings/localization",
			new { culture = "de-DE" },
			_clientToken);

		Assert.Multiple(() =>
		{
			Assert.That(folders.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(variables.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(createVariable.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(version.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(serverTime.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(integrationIcon.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
			Assert.That(filesystem.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(reveal.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(connectionInfo.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(updateAppearance.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(shutdown.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			// The queue carries pairing prompts, and a forged result would suppress the host's fallback.
			Assert.That(shellNotifications.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(reportShellNotification.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(devices.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			// Focus rules carry device ids and must not leak to viewer scope either.
			Assert.That(focusRules.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(applicationFocus.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(runningApplications.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(setFocusRule.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(deleteFocusRule.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(setStartupProfile.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(openProfileOnDevice.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(logging.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(pluginTokens.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(pluginSessions.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(localization.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(localizationSettings.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(updateLocalizationSettings.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
		});
	}

	[Test]
	public async Task Admin_scope_reaches_admin_routes_remotely()
	{
		var filesystem = await SendJson(HttpMethod.Post, "/api/filesystem/list", new { path = "" }, _adminToken);
		var appearance = await Send(HttpMethod.Get, "/api/settings/appearance", _adminToken);
		var devices = await Send(HttpMethod.Get, "/api/devices", _adminToken);
		var focusRules = await Send(HttpMethod.Get, "/api/folders/focus-rules", _adminToken);
		var applicationFocus = await Send(HttpMethod.Get, "/api/system/application-focus", _adminToken);
		var runningApplications = await Send(HttpMethod.Get, "/api/system/running-applications", _adminToken);
		var pluginTokens = await Send(HttpMethod.Get, "/api/plugin-tokens", _adminToken);
		var pluginSessions = await Send(HttpMethod.Get, "/api/plugin-sessions", _adminToken);
		var setFocusRule = await SendJson(HttpMethod.Put,
			$"/api/folders/{Guid.NewGuid()}/focus-rules",
			new
			{
				applicationIdentity = "notepad.exe", identityKind = "ExecutablePath",
				deviceId = Guid.NewGuid().ToString()
			},
			_adminToken);
		var deleteFocusRule = await Send(HttpMethod.Delete,
			$"/api/folders/{Guid.NewGuid()}/focus-rules/{Guid.NewGuid()}",
			_adminToken);
		var setStartupProfile = await SendJson(HttpMethod.Patch,
			$"/api/devices/{Guid.NewGuid()}/startup-profile",
			new { profileId = (string?)null },
			_adminToken);
		var openProfileOnDevice = await SendJson(HttpMethod.Post,
			$"/api/devices/{Guid.NewGuid()}/open-profile",
			new { profileId = "p1" },
			_adminToken);
		var logging = await Send(HttpMethod.Get, "/api/settings/logging", _adminToken);
		var localization = await Send(HttpMethod.Get, "/api/localization", _adminToken);
		var updateLocalizationSettings = await SendJson(HttpMethod.Put,
			"/api/settings/localization",
			new { culture = "de-DE" },
			_adminToken);

		Assert.Multiple(() =>
		{
			Assert.That(filesystem.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(appearance.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(devices.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(focusRules.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(applicationFocus.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(runningApplications.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(pluginTokens.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(pluginSessions.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(setFocusRule.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(deleteFocusRule.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(setStartupProfile.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(openProfileOnDevice.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(logging.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(localization.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(updateLocalizationSettings.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		});
	}

	[Test]
	public async Task Device_removal_requires_admin_scope_and_returns_result_errors_for_admin()
	{
		var id = Guid.NewGuid();
		var anonymous = await Send(HttpMethod.Delete, $"/api/devices/{id}");
		var client = await Send(HttpMethod.Delete, $"/api/devices/{id}", _clientToken);
		var admin = await Send(HttpMethod.Delete, $"/api/devices/{id}", _adminToken);
		var adminBody = await ReadJson(admin);

		Assert.Multiple(() =>
		{
			Assert.That(anonymous.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(client.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(admin.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(adminBody.GetProperty("success").GetBoolean(), Is.False);
			Assert.That(adminBody.GetProperty("error").GetProperty("code").GetString(), Is.EqualTo("NotFound"));
		});
	}

	[Test]
	public async Task Admin_can_remove_a_seeded_device_through_http_without_affecting_another_device()
	{
		var targetLogin = await LoginWithDevice("Target device");
		var unrelatedLogin = await LoginWithDevice("Unrelated device");
		var target = targetLogin.Body.GetProperty("device").GetProperty("deviceId").GetString()!;
		var unrelated = unrelatedLogin.Body.GetProperty("device").GetProperty("deviceId").GetString()!;
		var targetRefresh = ExtractCookie(targetLogin.Response, "md_refresh");

		var removal = await Send(HttpMethod.Delete, $"/api/devices/{target}", _adminToken);
		var removalBody = await ReadJson(removal);
		var remaining = await ReadJson(await Send(HttpMethod.Get, "/api/devices", _adminToken));
		var remainingIds = remaining.GetProperty("devices").EnumerateArray()
			.Select(device => device.GetProperty("id").GetString())
			.ToArray();
		var refreshAfterRemoval = await Send(HttpMethod.Post, "/api/auth/refresh", cookie: targetRefresh);

		Assert.Multiple(() =>
		{
			Assert.That(removal.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(removalBody.GetProperty("success").GetBoolean(), Is.True);
			Assert.That(remainingIds, Does.Not.Contain(target));
			Assert.That(remainingIds, Does.Contain(unrelated));
			Assert.That(refreshAfterRemoval.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
		});
	}

	[Test]
	public async Task Loopback_requests_are_implicitly_trusted_without_any_token()
	{
		var folders = await Send(HttpMethod.Get, "/api/folders", loopback: true);
		var filesystem = await SendJson(HttpMethod.Post, "/api/filesystem/list", new { path = "" }, loopback: true);

		Assert.Multiple(() =>
		{
			Assert.That(folders.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(filesystem.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		});
	}

	[Test]
	public async Task Ui_websocket_ticket_requires_client_auth_and_protocol_header_but_does_not_filter_origin()
	{
		var anonymous = await SendJson(HttpMethod.Post, "/api/ui-websocket/tickets", new { });
		var wrongProtocol = await SendJson(HttpMethod.Post, "/api/ui-websocket/tickets", new { }, _clientToken);

		using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ui-websocket/tickets")
		{
			Content = JsonContent.Create(new { })
		};
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _clientToken);
		request.Headers.Add("X-MacroDeck-Ui-Protocol", "1");
		request.Headers.Add("Origin", "https://reverse-proxy.example");
		var accepted = await _client.SendAsync(request);
		var json = await ReadJson(accepted);
		using var trailingSlashPreflight = new HttpRequestMessage(HttpMethod.Options, "/api/ui-websocket/tickets/");
		trailingSlashPreflight.Headers.Add("Origin", "https://attacker.example");
		trailingSlashPreflight.Headers.Add("Access-Control-Request-Method", "POST");
		trailingSlashPreflight.Headers.Add("Access-Control-Request-Headers",
			"authorization,content-type,x-macrodeck-ui-protocol");
		var preflight = await _client.SendAsync(trailingSlashPreflight);

		Assert.Multiple(() =>
		{
			Assert.That(anonymous.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(wrongProtocol.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
			Assert.That(accepted.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(json.GetProperty("value").GetString(), Has.Length.EqualTo(43));
			Assert.That(accepted.Headers.CacheControl?.NoStore, Is.True);
			Assert.That(accepted.Headers.Contains("Access-Control-Allow-Origin"), Is.False);
			Assert.That(preflight.Headers.Contains("Access-Control-Allow-Origin"), Is.False);
		});
	}

	[Test]
	public async Task Ui_websocket_negotiates_on_the_existing_listener_and_keeps_unknown_requests_recoverable()
	{
		using var ticketRequest = new HttpRequestMessage(HttpMethod.Post, "/api/ui-websocket/tickets")
		{
			Content = JsonContent.Create(new { })
		};
		ticketRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _clientToken);
		ticketRequest.Headers.Add("X-MacroDeck-Ui-Protocol", "1");
		var ticketResponse = await _client.SendAsync(ticketRequest);
		var ticket = (await ReadJson(ticketResponse)).GetProperty("value").GetString()!;

		var webSocketClient = _host.GetTestServer().CreateWebSocketClient();
		webSocketClient.SubProtocols.Add(UiWebSocketProtocol.SubProtocol);
		webSocketClient.ConfigureRequest = request =>
		{
			request.Headers[FakeConnectionStartupFilter.ShapeHeader] = nameof(FakeConnectionShape.PublicLan);
			request.Headers.Origin = "https://reverse-proxy.example";
		};
		using var socket = await webSocketClient.ConnectAsync(
			new Uri($"ws://localhost{UiWebSocketProtocol.Path}?ticket={Uri.EscapeDataString(ticket)}"),
			CancellationToken.None);

		await SendUiEnvelope(socket, new UiWebSocketEnvelope(1, "hello", null, "hello-1", null, null, null));
		var welcome = await ReceiveUiEnvelope(socket);
		await SendUiEnvelope(socket,
			new UiWebSocketEnvelope(1,
				"request",
				"NotAnOperation",
				"request-1",
				null,
				JsonSerializer.SerializeToElement(Array.Empty<object>()),
				null));
		var error = await ReceiveUiEnvelope(socket);
		await SendUiEnvelope(socket,
			new UiWebSocketEnvelope(1,
				"request",
				"RenderLabelPreview",
				"request-2",
				null,
				JsonSerializer.SerializeToElement(new object[] { new { } }),
				null));
		var forbidden = await ReceiveUiEnvelope(socket);
		await SendUiEnvelope(socket,
			new UiWebSocketEnvelope(1,
				"message",
				"UnsubscribeLabel",
				null,
				null,
				JsonSerializer.SerializeToElement(UnsubscribeLabelArguments),
				null));
		await SendUiEnvelope(socket, new UiWebSocketEnvelope(1, "ping", null, "ping-1", null, null, null));
		var pong = await ReceiveUiEnvelope(socket);

		Assert.Multiple(() =>
		{
			Assert.That(socket.SubProtocol, Is.EqualTo(UiWebSocketProtocol.SubProtocol));
			Assert.That(welcome.Kind, Is.EqualTo("welcome"));
			Assert.That(welcome.CorrelationId, Is.EqualTo("hello-1"));
			Assert.That(error.Kind, Is.EqualTo("error"));
			Assert.That(error.CorrelationId, Is.EqualTo("request-1"));
			Assert.That(error.Error?.Code, Is.EqualTo("unknown_type"));
			Assert.That(forbidden.Kind, Is.EqualTo("error"));
			Assert.That(forbidden.CorrelationId, Is.EqualTo("request-2"));
			Assert.That(forbidden.Error?.Code, Is.EqualTo("forbidden"));
			Assert.That(pong.Kind, Is.EqualTo("pong"));
			Assert.That(pong.Id, Is.Null);
			Assert.That(pong.CorrelationId, Is.EqualTo("ping-1"));
		});
		socket.Abort();
	}

	[Test]
	public async Task Ui_websocket_ticket_is_consumed_even_when_subprotocol_negotiation_fails()
	{
		var ticket = await MintUiTicket();
		var withoutProtocol = _host.GetTestServer().CreateWebSocketClient();
		withoutProtocol.ConfigureRequest = ConfigurePublicUiWebSocketRequest;
		var first = Assert.ThrowsAsync<InvalidOperationException>(async () =>
			await withoutProtocol.ConnectAsync(new Uri($"ws://localhost{UiWebSocketProtocol.Path}?ticket={ticket}"),
				CancellationToken.None));

		var withProtocol = _host.GetTestServer().CreateWebSocketClient();
		withProtocol.SubProtocols.Add(UiWebSocketProtocol.SubProtocol);
		withProtocol.ConfigureRequest = ConfigurePublicUiWebSocketRequest;
		var replay = Assert.ThrowsAsync<InvalidOperationException>(async () =>
			await withProtocol.ConnectAsync(new Uri($"ws://localhost{UiWebSocketProtocol.Path}?ticket={ticket}"),
				CancellationToken.None));

		Assert.Multiple(() =>
		{
			Assert.That(first!.Message, Does.Contain("400"));
			Assert.That(replay!.Message, Does.Contain("400"));
		});
	}

	[TestCase(true)]
	[TestCase(false)]
	public async Task Ui_websocket_rejects_oversized_hello_identifiers(bool useId)
	{
		var ticket = await MintUiTicket();
		var webSocketClient = _host.GetTestServer().CreateWebSocketClient();
		webSocketClient.SubProtocols.Add(UiWebSocketProtocol.SubProtocol);
		webSocketClient.ConfigureRequest = ConfigurePublicUiWebSocketRequest;
		using var socket = await webSocketClient.ConnectAsync(
			new Uri($"ws://localhost{UiWebSocketProtocol.Path}?ticket={ticket}"),
			CancellationToken.None);
		var oversized = new string('x', 129);
		await SendUiEnvelope(socket,
			new UiWebSocketEnvelope(1,
				"hello",
				null,
				useId ? oversized : null,
				useId ? null : oversized,
				null,
				null));

		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		var result = await socket.ReceiveAsync(new byte[256], timeout.Token);

		Assert.That(result.MessageType, Is.EqualTo(WebSocketMessageType.Close));
		Assert.That(result.CloseStatus, Is.EqualTo(WebSocketCloseStatus.PolicyViolation));
	}

	[Test]
	public async Task Spa_shells_are_anonymous_at_root_and_admin()
	{
		var webClient = await Send(HttpMethod.Get, "/");
		var webClientDeepLink = await Send(HttpMethod.Get, "/some/client/route");
		var adminRoot = await Send(HttpMethod.Get, "/admin");
		var adminDeepLink = await Send(HttpMethod.Get, "/admin/settings/security");

		Assert.Multiple(() =>
		{
			Assert.That(webClient.StatusCode,
				Is.Not.EqualTo(HttpStatusCode.Unauthorized).And.Not.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(webClientDeepLink.StatusCode,
				Is.Not.EqualTo(HttpStatusCode.Unauthorized).And.Not.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(adminRoot.StatusCode,
				Is.Not.EqualTo(HttpStatusCode.Unauthorized).And.Not.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(adminDeepLink.StatusCode,
				Is.Not.EqualTo(HttpStatusCode.Unauthorized).And.Not.EqualTo(HttpStatusCode.Forbidden));
		});
	}

	[Test]
	public async Task OAuth_callback_stays_anonymous()
	{
		var response = await Send(HttpMethod.Get, "/api/integrations/oauth/callback?state=unknown&code=x");

		Assert.That(response.StatusCode,
			Is.Not.EqualTo(HttpStatusCode.Unauthorized).And.Not.EqualTo(HttpStatusCode.Forbidden));
	}

	[Test]
	public async Task Wrong_password_is_rejected()
	{
		var response = await SendJson(HttpMethod.Post,
			"/api/auth/login",
			new { username = "admin", password = "wrong-password", scope = "client" });

		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
	}

	[Test]
	public async Task Login_always_issues_a_refresh_cookie_that_outlives_the_browser_session()
	{
		var login = await SendJson(HttpMethod.Post,
			"/api/auth/login",
			new { username = "admin", password = "password123", scope = "client" });

		var expires = RefreshCookieExpiry(login);

		Assert.That(expires,
			Is.Not.Null,
			"a session cookie ends when the browser closes, which is exactly what issue #839 removes");
		Assert.That(expires!.Value - DateTimeOffset.UtcNow,
			Is.EqualTo(AuthDefaults.RefreshTokenLifetime).Within(TimeSpan.FromHours(1)));
	}

	[Test]
	public async Task A_client_that_still_asks_not_to_stay_logged_in_stays_logged_in_anyway()
	{
		// A client compiled before issue #839 keeps sending the flag. The host ignores it rather than
		// honouring a choice that no longer exists.
		var login = await SendJson(HttpMethod.Post,
			"/api/auth/login",
			new { username = "admin", password = "password123", scope = "client", stayLoggedIn = false });

		var expires = RefreshCookieExpiry(login);

		Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		Assert.That(expires, Is.Not.Null);
		Assert.That(expires!.Value - DateTimeOffset.UtcNow,
			Is.EqualTo(AuthDefaults.RefreshTokenLifetime).Within(TimeSpan.FromHours(1)));
	}

	[Test]
	public async Task Refresh_rotates_and_rejects_a_reused_cookie()
	{
		var login = await SendJson(HttpMethod.Post,
			"/api/auth/login",
			new { username = "admin", password = "password123", scope = "client" });
		var refreshCookie = ExtractCookie(login, "md_refresh");

		var refresh = await Send(HttpMethod.Post, "/api/auth/refresh", cookie: refreshCookie);
		var reuse = await Send(HttpMethod.Post, "/api/auth/refresh", cookie: refreshCookie);

		Assert.Multiple(() =>
		{
			Assert.That(refreshCookie, Is.Not.Null);
			Assert.That(refresh.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(reuse.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
		});
	}

	[Test]
	public async Task Access_cookie_authenticates_media_gets_but_never_mutations()
	{
		var login = await SendJson(HttpMethod.Post,
			"/api/auth/login",
			new { username = "admin", password = "password123", scope = "client" });
		var accessCookie = ExtractCookie(login, "md_access");

		var mediaGet = await Send(HttpMethod.Get, $"/api/icons/{Guid.NewGuid()}/image", cookie: accessCookie);
		var mutation = await SendJson(HttpMethod.Post, "/api/actions/execute", new { }, cookie: accessCookie);
		var status = await ReadJson(await Send(HttpMethod.Get, "/api/auth/status", cookie: accessCookie));

		Assert.Multiple(() =>
		{
			Assert.That(accessCookie, Is.Not.Null);
			Assert.That(mediaGet.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
			Assert.That(mutation.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			// Cookie authentication must never read as transport-level trust.
			Assert.That(status.GetProperty("authenticated").GetBoolean(), Is.True);
			Assert.That(status.GetProperty("trusted").GetBoolean(), Is.False);
		});
	}

	private static StartupReadiness CompletedStartupReadiness()
	{
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();
		return readiness;
	}

	private async Task<string> LoginAndGetToken(string scope)
	{
		var response = await SendJson(HttpMethod.Post,
			"/api/auth/login",
			new { username = "admin", password = "password123", scope });
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"login with scope {scope}");
		var json = await ReadJson(response);

		return json.GetProperty("accessToken").GetString()!;
	}

	private async Task<(JsonElement Body, HttpResponseMessage Response)> LoginWithDevice(string proposedName)
	{
		var response = await SendJson(HttpMethod.Post,
			"/api/auth/login",
			new
			{
				username = "admin",
				password = "password123",
				scope = "client",
				device = new
				{
					clientType = "web-client",
					proposedName,
					platform = "Windows",
					browser = "Chrome",
					formFactor = "desktop",
					appVersion = "test"
				}
			});
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

		return (await ReadJson(response), response);
	}

	private Task<HttpResponseMessage> Send(
		HttpMethod method,
		string path,
		string? bearerToken = null,
		bool loopback = false,
		string? cookie = null)
		=> SendCore(method, path, null, bearerToken, loopback, cookie);

	private Task<HttpResponseMessage> SendJson(
		HttpMethod method,
		string path,
		object body,
		string? bearerToken = null,
		bool loopback = false,
		string? cookie = null)
		=> SendCore(method, path, body, bearerToken, loopback, cookie);

	private async Task<HttpResponseMessage> SendCore(
		HttpMethod method,
		string path,
		object? body,
		string? bearerToken,
		bool loopback,
		string? cookie)
	{
		using var request = new HttpRequestMessage(method, path);
		if (body is not null)
		{
			request.Content = JsonContent.Create(body);
		}

		if (bearerToken is not null)
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
		}

		if (loopback)
		{
			request.Headers.Add(FakeConnectionStartupFilter.ShapeHeader, nameof(FakeConnectionShape.Loopback));
		}

		if (cookie is not null)
		{
			request.Headers.Add("Cookie", cookie);
		}

		return await _client.SendAsync(request);
	}

	private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
		=> JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

	/// <summary>The refresh cookie's absolute expiry, or null when it is a session cookie.</summary>
	private static DateTimeOffset? RefreshCookieExpiry(HttpResponseMessage response)
	{
		var setCookie = response.Headers.TryGetValues("Set-Cookie", out var cookies)
			? cookies.FirstOrDefault(c => c.StartsWith("md_refresh_", StringComparison.Ordinal))
			: null;
		var expires = setCookie?.Split(';')
			.Select(part => part.Trim())
			.FirstOrDefault(part => part.StartsWith("expires=", StringComparison.OrdinalIgnoreCase));

		return expires is null
			? null
			: DateTimeOffset.Parse(expires["expires=".Length..], CultureInfo.InvariantCulture);
	}

	private static string? ExtractCookie(HttpResponseMessage response, string name)
	{
		if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
		{
			return null;
		}

		// The cookies are named per listener (md_access_<port>), so the caller names the family rather than
		// the exact cookie.
		var match = cookies.FirstOrDefault(c => c.StartsWith(name + "=", StringComparison.Ordinal) ||
			c.StartsWith(name + "_", StringComparison.Ordinal));

		return match?.Split(';')[0];
	}

	private async Task<string> MintUiTicket()
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ui-websocket/tickets")
		{
			Content = JsonContent.Create(new { })
		};
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _clientToken);
		request.Headers.Add("X-MacroDeck-Ui-Protocol", "1");
		var response = await _client.SendAsync(request);
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		return (await ReadJson(response)).GetProperty("value").GetString()!;
	}

	private static void ConfigurePublicUiWebSocketRequest(Microsoft.AspNetCore.Http.HttpRequest request)
	{
		request.Headers[FakeConnectionStartupFilter.ShapeHeader] = nameof(FakeConnectionShape.PublicLan);
		request.Headers.Origin = "https://reverse-proxy.example";
	}

	private static async Task SendUiEnvelope(WebSocket socket, UiWebSocketEnvelope envelope)
	{
		var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, UiWebSocketProtocol.Json);
		await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
	}

	private static async Task<UiWebSocketEnvelope> ReceiveUiEnvelope(WebSocket socket)
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		var bytes = new byte[UiWebSocketProtocol.MaxMessageBytes];
		var received = await socket.ReceiveAsync(bytes, timeout.Token);
		Assert.That(received.EndOfMessage, Is.True);
		return JsonSerializer.Deserialize<UiWebSocketEnvelope>(bytes.AsSpan(0, received.Count),
			UiWebSocketProtocol.Json)!;
	}
}
