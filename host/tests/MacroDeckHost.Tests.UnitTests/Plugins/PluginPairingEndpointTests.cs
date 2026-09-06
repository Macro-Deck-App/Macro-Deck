using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Plugins.Pairing;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Tests.UnitTests.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[NonParallelizable]
public class PluginPairingEndpointTests
{
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

		var enableDeveloperMode = await Send(HttpMethod.Put,
			"/api/settings/developer",
			shape: FakeConnectionShape.PublicLan,
			bearerToken: _adminToken,
			body: new { enabled = true });
		Assert.That(enableDeveloperMode.StatusCode, Is.EqualTo(HttpStatusCode.OK));
	}

	[SetUp]
	public void SetUp()
	{
		// The throttle buckets are process-wide singletons shared by every test in this fixture; clear
		// them before each test so a lockout left behind by one throttling test cannot fail an unrelated
		// one.
		var throttle = _host.Services.GetRequiredKeyedService<Application.Auth.LoginThrottle>("plugin");
		throttle.RegisterSuccess("plugin-pairing");
		throttle.RegisterSuccess("plugin-pairing-redeem");
	}

	[TearDown]
	public async Task TearDown()
	{
		// The pairing request store is a process-wide singleton with a small global cap (5 by default),
		// shared by every test in this fixture. A test that deliberately leaves requests pending (to
		// exercise the cap or the duplicate check) would otherwise starve every later test of capacity
		// for the rest of the fixture's real-time 5-minute request lifetime.
		var pending = await Send(HttpMethod.Get, "/api/plugin-pairing/requests", shape: FakeConnectionShape.Loopback);
		if (pending.StatusCode != HttpStatusCode.OK)
		{
			return;
		}

		foreach (var request in (await ReadJson(pending)).GetProperty("requests").EnumerateArray())
		{
			var requestId = request.GetProperty("requestId").GetString()!;
			await Send(HttpMethod.Post,
				$"/api/plugin-pairing/requests/{requestId}/reject",
				shape: FakeConnectionShape.Loopback);
		}
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

	/// <summary>Issue #752: a prompt behind a window nobody is looking at is a prompt nobody sees, so a
	/// pending request also leaves something in the notification list. Approving it must not leave a
	/// second one behind - the pairing pushes fire on approve and reject too.</summary>
	[Test]
	public async Task A_pending_pairing_request_is_notified_once_and_approving_it_adds_nothing()
	{
		var notifications = _host.Services.GetRequiredService<IUserNotificationStore>();
		notifications.DismissAll();

		var (verifier, challenge) = NewPkcePair();
		var create = await CreatePairingRequestAsync("com.example.notified", challenge);
		Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.Created));
		var requestId = (await ReadJson(create)).GetProperty("requestId").GetString()!;

		var afterCreate = notifications.Snapshot().Count;

		var approve = await Send(HttpMethod.Post,
			$"/api/plugin-pairing/requests/{requestId}/approve",
			shape: FakeConnectionShape.Loopback,
			body: new { replaceExistingRegistration = false });
		Assert.That(approve.StatusCode, Is.EqualTo(HttpStatusCode.OK));

		await Send(HttpMethod.Post,
			$"/api/plugins/pairing/{requestId}/redemption",
			shape: FakeConnectionShape.Loopback,
			body: new { codeVerifier = verifier });

		Assert.Multiple(() =>
		{
			Assert.That(afterCreate, Is.EqualTo(1));
			Assert.That(notifications.Snapshot(), Has.Count.EqualTo(1));
		});
	}

	/// <summary>Issue #752: the developer flipping this switch has to be enough on its own. The plugin
	/// side can only notice without a restart if the host answers from the live setting, so the same
	/// endpoint must report both answers within one running host.</summary>
	[Test]
	public async Task Protocol_discovery_reports_developer_mode_as_it_is_switched()
	{
		try
		{
			await SetDeveloperMode(false);
			var whileOff = await ReadProtocolDescriptor();

			await SetDeveloperMode(true);
			var whileOn = await ReadProtocolDescriptor();

			Assert.Multiple(() =>
			{
				Assert.That(whileOff.GetProperty("pairing").GetProperty("developerModeEnabled").GetBoolean(),
					Is.False);
				Assert.That(whileOn.GetProperty("pairing").GetProperty("developerModeEnabled").GetBoolean(), Is.True);

				Assert.That(whileOff.GetProperty("enrollment").GetProperty("developerModeEnabled").GetBoolean(),
					Is.False);
				Assert.That(whileOn.GetProperty("enrollment").GetProperty("developerModeEnabled").GetBoolean(),
					Is.True);

				// Unchanged either way: it says this host implements pairing, not that pairing is on.
				Assert.That(whileOff.GetProperty("pairing").GetProperty("supported").GetBoolean(), Is.True);
				Assert.That(whileOn.GetProperty("pairing").GetProperty("supported").GetBoolean(), Is.True);
			});
		}
		finally
		{
			await SetDeveloperMode(true);
		}
	}

	/// <summary>Issue #753 turned Developer Mode into a kill switch for enrolment and session creation.
	/// Pairing was already gated on it, and must stay that way.</summary>
	[Test]
	public async Task Creating_a_pairing_request_is_still_refused_while_developer_mode_is_off()
	{
		var throttle = _host.Services.GetRequiredKeyedService<Application.Auth.LoginThrottle>("plugin");
		try
		{
			await SetDeveloperMode(false);
			var (_, challenge) = NewPkcePair();

			var response = await CreatePairingRequestAsync($"com.example.off{Guid.NewGuid():N}", challenge);

			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
		}
		finally
		{
			await SetDeveloperMode(true);
			throttle.RegisterSuccess("plugin-pairing");
		}
	}

	private async Task SetDeveloperMode(bool enabled)
	{
		var response = await Send(HttpMethod.Put,
			"/api/settings/developer",
			shape: FakeConnectionShape.PublicLan,
			bearerToken: _adminToken,
			body: new { enabled });
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
	}

	private async Task<JsonElement> ReadProtocolDescriptor()
	{
		var response = await Send(HttpMethod.Get, "/api/plugins/protocol", shape: FakeConnectionShape.Loopback);
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		return await ReadJson(response);
	}

	private static readonly (HttpMethod Method, string Path)[] _desktopRoutes =
	[
		(HttpMethod.Get, "/api/plugin-pairing/requests"),
		(HttpMethod.Post, "/api/plugin-pairing/requests/whatever/approve"),
		(HttpMethod.Post, "/api/plugin-pairing/requests/whatever/reject"),
		(HttpMethod.Get, "/api/plugin-pairing/registrations"),
		(HttpMethod.Post, "/api/plugin-pairing/registrations/com.example.whatever/revoke")
	];

	[Test]
	public async Task Every_desktop_pairing_route_refuses_every_non_loopback_shape_even_with_a_valid_admin_token()
	{
		Assert.Multiple(async () =>
		{
			foreach (var (method, path) in _desktopRoutes)
			{
				foreach (var shape in new[]
					{
						FakeConnectionShape.PublicLan, FakeConnectionShape.LanOnPrivatePort,
						FakeConnectionShape.LoopbackOnPublicPort
					})
				{
					var response = await Send(method,
						path,
						shape: shape,
						bearerToken: _adminToken,
						body: method == HttpMethod.Post ? new { replaceExistingRegistration = false } : null);

					Assert.That(response.StatusCode,
						Is.EqualTo(HttpStatusCode.Forbidden),
						$"{method} {path} on {shape} with a valid admin token");
				}
			}
		});
	}

	[Test]
	public async Task A_loopback_remote_succeeds_on_the_desktop_pairing_routes()
	{
		var response = await Send(HttpMethod.Get, "/api/plugin-pairing/requests", shape: FakeConnectionShape.Loopback);

		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
	}

	[Test]
	public async Task A_loopback_trusted_approve_and_reject_still_succeeds_with_an_Origin_header()
	{
		// PluginPairingRequestsController deliberately does not call PluginEndpointGate.TryReject - its
		// own doc comment says PluginBrowserGuard would 403 the desktop UI's own POSTs, since a real
		// browser always sends Origin on unsafe methods. This is what makes that comment enforceable:
		// if TryReject (or an equivalent Origin/browser check) were ever added to this controller, these
		// requests would start failing instead of the real desktop app breaking silently.
		var originHeader = new Dictionary<string, string> { ["Origin"] = "https://localhost:4200" };

		var (_, approveChallenge) = NewPkcePair();
		var approveCreated = await CreatePairingRequestAsync($"com.example.origin{Guid.NewGuid():N}", approveChallenge);
		Assert.That(approveCreated.StatusCode, Is.EqualTo(HttpStatusCode.Created));
		var approveRequestId = (await ReadJson(approveCreated)).GetProperty("requestId").GetString()!;

		var approve = await Send(HttpMethod.Post,
			$"/api/plugin-pairing/requests/{approveRequestId}/approve",
			shape: FakeConnectionShape.Loopback,
			body: new { replaceExistingRegistration = false },
			extraHeaders: originHeader);
		Assert.That(approve.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		Assert.That((await ReadJson(approve)).GetProperty("success").GetBoolean(), Is.True);

		var (_, rejectChallenge) = NewPkcePair();
		var rejectCreated = await CreatePairingRequestAsync($"com.example.origin{Guid.NewGuid():N}", rejectChallenge);
		Assert.That(rejectCreated.StatusCode, Is.EqualTo(HttpStatusCode.Created));
		var rejectRequestId = (await ReadJson(rejectCreated)).GetProperty("requestId").GetString()!;

		var reject = await Send(HttpMethod.Post,
			$"/api/plugin-pairing/requests/{rejectRequestId}/reject",
			shape: FakeConnectionShape.Loopback,
			extraHeaders: originHeader);
		Assert.That(reject.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		Assert.That((await ReadJson(reject)).GetProperty("success").GetBoolean(), Is.True);
	}

	// Named, scoped and asserted around what this fixture can actually observe: every plugin id below
	// is distinct (LiveOnly), so this never exercises the same-plugin-id dedupe
	// (Duplicate_creates_for_the_same_plugin_id_are_rate_limited covers that) or the LoginThrottle's own
	// failure-count lockout - it hits PluginPairingRequestStore's MaxPendingRequests cap (5 by default)
	// well before enough failures accumulate to lock the throttle out. That is confirmed, not assumed:
	// PluginPairingController.RateLimited() answers a capacity-exceeded create with a fixed
	// PluginPairingOptions.PollInterval retry-after, whereas a throttle lockout's retry-after would grow
	// with the lockout's own backoff - so the retry-after asserted below pins this down as the capacity
	// path specifically.
	//
	// Recovery after the window is deliberately not asserted here: this fixture starts a full Startup
	// host, which registers TimeProvider.System for the "plugin" LoginThrottle (see Startup.cs), not a
	// test-controllable clock. Advancing past MaxPendingRequests' recovery would mean a real-time sleep
	// keyed to PluginPairingOptions.RequestLifetime (5 minutes by default) - LoginThrottleTests.cs is
	// where recovery-after-lockout is exercised, against LoginThrottle directly with a ManualTimeProvider.
	[Test]
	public async Task Exceeding_the_pending_request_cap_is_rejected_with_the_fixed_capacity_retry_after()
	{
		string LiveOnly(int i) => $"com.example.cap{i}z{Guid.NewGuid():N}";

		var throttle = _host.Services.GetRequiredKeyedService<Application.Auth.LoginThrottle>("plugin");
		var options = _host.Services.GetRequiredService<PluginPairingOptions>();
		try
		{
			HttpResponseMessage? overCap = null;
			for (var i = 0; i < 20 && overCap is null; i++)
			{
				var (_, challenge) = NewPkcePair();
				var response = await CreatePairingRequestAsync(LiveOnly(i), challenge);
				if (response.StatusCode == HttpStatusCode.TooManyRequests)
				{
					overCap = response;
				}
			}

			Assert.That(overCap, Is.Not.Null);
			Assert.That(overCap!.Headers.RetryAfter?.Delta,
				Is.EqualTo(options.PollInterval).Within(TimeSpan.FromSeconds(1)),
				"a capacity-exceeded 429 carries PluginPairingOptions.PollInterval as its retry-after, not the " +
				"throttle's own (larger, backed-off) lockout - this is what distinguishes the two 429 causes.");
		}
		finally
		{
			throttle.RegisterSuccess("plugin-pairing");
		}
	}

	[Test]
	public async Task Duplicate_creates_for_the_same_plugin_id_are_rate_limited()
	{
		var throttle = _host.Services.GetRequiredKeyedService<Application.Auth.LoginThrottle>("plugin");
		try
		{
			var pluginId = $"com.example.dup{Guid.NewGuid():N}";
			var (_, firstChallenge) = NewPkcePair();
			var first = await CreatePairingRequestAsync(pluginId, firstChallenge);
			Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.Created));

			var (_, secondChallenge) = NewPkcePair();
			var duplicate = await CreatePairingRequestAsync(pluginId, secondChallenge);

			Assert.Multiple(() =>
			{
				Assert.That(duplicate.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
				Assert.That(duplicate.Headers.RetryAfter, Is.Not.Null);
			});
		}
		finally
		{
			throttle.RegisterSuccess("plugin-pairing");
		}
	}

	[Test]
	public async Task Pending_and_status_bodies_never_carry_a_secret_or_the_code_challenge_only_redemption_does()
	{
		var pluginId = $"com.example.leak{Guid.NewGuid():N}";
		var (verifier, challenge) = NewPkcePair();

		var created = await CreatePairingRequestAsync(pluginId, challenge);
		Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created));
		var createdJson = await ReadJson(created);
		var requestId = createdJson.GetProperty("requestId").GetString()!;
		AssertNoSecretOrChallenge(createdJson, challenge);

		var status = await Send(HttpMethod.Get,
			$"/api/plugins/pairing/{requestId}",
			shape: FakeConnectionShape.Loopback);
		AssertNoSecretOrChallenge(await ReadJson(status), challenge);

		var pending = await Send(HttpMethod.Get, "/api/plugin-pairing/requests", shape: FakeConnectionShape.Loopback);
		var pendingBody = await pending.Content.ReadAsStringAsync();
		Assert.That(pendingBody, Does.Not.Contain(challenge));

		var pendingJson = await ReadJson(pending);
		var pendingItem = pendingJson.GetProperty("requests").EnumerateArray()
			.Single(item => item.GetProperty("requestId").GetString() == requestId);
		Assert.That(pendingItem.GetProperty("arrivedOnPublicListener").GetBoolean(),
			Is.False,
			"a request created over the trusted loopback listener must not be reported as public");

		var publicPluginId = $"com.example.leak{Guid.NewGuid():N}";
		var (_, publicChallenge) = NewPkcePair();
		var createdOnPublicListener = await Send(HttpMethod.Post,
			"/api/plugins/pairing",
			shape: FakeConnectionShape.LoopbackOnPublicPort,
			body: new
			{
				pluginId = publicPluginId,
				displayName = "Example Plugin",
				codeChallenge = publicChallenge,
				codeChallengeMethod = "S256"
			});
		Assert.That(createdOnPublicListener.StatusCode, Is.EqualTo(HttpStatusCode.Created));
		var publicRequestId = (await ReadJson(createdOnPublicListener)).GetProperty("requestId").GetString()!;

		var pendingAfterPublic = await Send(HttpMethod.Get,
			"/api/plugin-pairing/requests",
			shape: FakeConnectionShape.Loopback);
		var publicPendingItem = (await ReadJson(pendingAfterPublic)).GetProperty("requests").EnumerateArray()
			.Single(item => item.GetProperty("requestId").GetString() == publicRequestId);
		Assert.That(publicPendingItem.GetProperty("arrivedOnPublicListener").GetBoolean(),
			Is.True,
			"a request that arrived on the public listener must be reported as such");

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
		var redemptionJson = await ReadJson(redemption);
		Assert.That(redemptionJson.GetProperty("pluginSecret").GetString(), Is.Not.Null.And.Not.Empty);
	}

	private static void AssertNoSecretOrChallenge(JsonElement json, string challenge)
	{
		var raw = json.GetRawText();
		Assert.Multiple(() =>
		{
			Assert.That(raw, Does.Not.Contain(challenge));
			Assert.That(raw.Contains("pluginSecret", StringComparison.OrdinalIgnoreCase), Is.False);
			Assert.That(raw.Contains("codeChallenge", StringComparison.OrdinalIgnoreCase), Is.False);
		});
	}

	private static (string Verifier, string Challenge) NewPkcePair()
	{
		var verifier = $"verifier-{Guid.NewGuid():N}-{Guid.NewGuid():N}";
		var digest = SHA256.HashData(Encoding.UTF8.GetBytes(verifier));
		var challenge = Convert.ToBase64String(digest).TrimEnd('=').Replace('+', '-').Replace('/', '_');
		return (verifier, challenge);
	}

	private Task<HttpResponseMessage> CreatePairingRequestAsync(string pluginId, string challenge)
		=> Send(HttpMethod.Post,
			"/api/plugins/pairing",
			shape: FakeConnectionShape.Loopback,
			body: new
			{
				pluginId, displayName = "Example Plugin", codeChallenge = challenge, codeChallengeMethod = "S256"
			});

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
