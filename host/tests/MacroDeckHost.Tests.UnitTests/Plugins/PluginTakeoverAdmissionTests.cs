using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Auth;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Runtime;
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
public class PluginTakeoverAdmissionTests
{
	private readonly FakePluginInstallationCatalog _catalog = new();
	private readonly SessionCreateHook _hook = new();
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
					services.RemoveAll<IPluginInstallationCatalog>();
					services.AddSingleton<IPluginInstallationCatalog>(_catalog);
					services.RemoveAll<IPluginSessionService>();
					services.AddScoped<IPluginSessionService>(provider => new HookedSessionService(
						ActivatorUtilities.CreateInstance<PluginSessionService>(provider),
						provider,
						_hook));
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
		Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		_adminToken = (await ReadJson(login)).GetProperty("accessToken").GetString()!;

		await SetDeveloperMode(true);
	}

	[SetUp]
	public void SetUp()
	{
		var throttle = _host.Services.GetRequiredKeyedService<Application.Auth.LoginThrottle>("plugin");
		throttle.RegisterSuccess("plugin-enroll");
		throttle.RegisterSuccess("plugin-pairing");
		throttle.RegisterSuccess("plugin-pairing-redeem");
		_hook.BeforeCreate = null;
	}

	[TearDown]
	public async Task TearDown()
	{
		await SetDeveloperMode(true);

		var pending = await Send(HttpMethod.Get, "/api/plugin-pairing/requests", FakeConnectionShape.Loopback);
		foreach (var request in (await ReadJson(pending)).GetProperty("requests").EnumerateArray())
		{
			await Send(HttpMethod.Post,
				$"/api/plugin-pairing/requests/{request.GetProperty("requestId").GetString()}/reject",
				FakeConnectionShape.Loopback);
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

	[Test]
	public async Task Pairing_an_installed_id_is_accepted_and_the_takeover_is_confirmed_separately()
	{
		var pluginId = InstallNewPlugin();
		var (verifier, challenge) = NewPkcePair();

		var created = await CreatePairingRequestAsync(pluginId, challenge);
		var requestId = (await ReadJson(created)).GetProperty("requestId").GetString()!;
		var pendingItem = await PendingItemAsync(requestId);
		var unconfirmed = await ApproveAsync(requestId, takeOverInstalledPlugin: false);
		var unconfirmedJson = await ReadJson(unconfirmed);
		var confirmed = await ApproveAsync(requestId, takeOverInstalledPlugin: true);
		var redemption = await RedeemAsync(requestId, verifier);
		var registration = await PairedRegistrationAsync(pluginId);
		var runtime = await RuntimeSnapshotAsync(pluginId);

		Assert.Multiple(async () =>
		{
			Assert.That(created.StatusCode,
				Is.EqualTo(HttpStatusCode.Created),
				"an SDK that pairs an installed id is now answered with a prompt, not a 409");
			Assert.That(pendingItem.GetProperty("takesOverInstalledPlugin").GetBoolean(), Is.True);
			Assert.That(unconfirmedJson.GetProperty("success").GetBoolean(), Is.False);
			Assert.That(unconfirmedJson.GetProperty("error").GetProperty("code").GetString(),
				Is.EqualTo("takeover_not_confirmed"));
			Assert.That((await ReadJson(confirmed)).GetProperty("success").GetBoolean(), Is.True);
			Assert.That(redemption.StatusCode, Is.EqualTo(HttpStatusCode.Created));
			Assert.That(registration.GetProperty("takesOverInstalledPlugin").GetBoolean(), Is.True);
			Assert.That(runtime.GetProperty("takenOverByDevelopmentBuild").GetBoolean(), Is.True);
		});
	}

	[Test]
	public async Task During_a_takeover_the_development_session_is_admitted_and_a_managed_launch_is_not()
	{
		var (pluginId, secret) = await TakeOverAsync();
		var launchSecret = _host.Services.GetRequiredService<IPluginLaunchTokenService>()
			.Mint(pluginId, $"launch-{Guid.NewGuid():N}", "Installed Plugin", "1.0.0");

		var development = await CreateSessionAsync(pluginId, secret);
		var managed = await CreateSessionAsync(pluginId, launchSecret);
		var sessions = SessionsOf(pluginId);

		Assert.Multiple(() =>
		{
			Assert.That(development.StatusCode,
				Is.EqualTo(HttpStatusCode.Created),
				"a live launch token for the id must not hold the development build out during a takeover");
			Assert.That(managed.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(sessions, Has.Count.EqualTo(1));
			Assert.That(sessions[0].Origin,
				Is.EqualTo(PluginSessionOrigin.SelfRegistered),
				"the managed launch must not have replaced the development session");
		});
	}

	[Test]
	public async Task A_second_takeover_after_the_first_was_revoked_is_admitted()
	{
		var (pluginId, _) = await TakeOverAsync();
		using (var scope = _host.Services.CreateScope())
		{
			await scope.ServiceProvider.GetRequiredService<IPluginRegistrationService>().Revoke(pluginId);
		}

		var secret = await PairAsync(pluginId, takeOverInstalledPlugin: true);
		var development = await CreateSessionAsync(pluginId, secret);

		Assert.Multiple(() =>
		{
			Assert.That(development.StatusCode, Is.EqualTo(HttpStatusCode.Created));
			Assert.That(SessionsOf(pluginId).Select(session => session.Origin),
				Is.EqualTo(new[] { PluginSessionOrigin.SelfRegistered }));
		});
	}

	[Test]
	public async Task A_credential_for_an_installed_id_without_a_live_takeover_is_refused()
	{
		var pluginId = InstallNewPlugin();
		string secret;
		using (var scope = _host.Services.CreateScope())
		{
			var registered = await scope.ServiceProvider.GetRequiredService<IPluginRegistrationService>()
				.Register(pluginId, "Development Build", null, PluginRegistrationOrigins.Pairing, allowInstalledId: true);
			secret = registered.PluginSecret!;
		}

		var refused = await CreateSessionAsync(pluginId, secret);

		Assert.Multiple(() =>
		{
			Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(SessionsOf(pluginId), Is.Empty);
		});
	}

	[Test]
	public async Task A_revoke_racing_session_creation_leaves_no_development_session()
	{
		var (pluginId, secret) = await TakeOverAsync();
		_hook.BeforeCreate = async (provider, identity) =>
		{
			if (identity.PluginId == pluginId)
			{
				await provider.GetRequiredService<IPluginRegistrationService>().Revoke(pluginId);
			}
		};

		var response = await CreateSessionAsync(pluginId, secret);

		Assert.Multiple(() =>
		{
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(SessionsOf(pluginId), Is.Empty);
			Assert.That(_host.Services.GetRequiredService<IPluginTakeoverRegistry>().IsActive(pluginId), Is.False);
		});
	}

	[Test]
	public async Task Switching_developer_mode_off_ends_a_takeover_and_keeps_an_ordinary_paired_credential()
	{
		var (takenOverId, _) = await TakeOverAsync();
		var ordinaryId = NewPluginId();
		await PairAsync(ordinaryId, takeOverInstalledPlugin: false);

		await SetDeveloperMode(false);
		await SetDeveloperMode(true);
		var paired = await PairedRegistrationIdsAsync();

		Assert.Multiple(() =>
		{
			Assert.That(paired, Does.Contain(ordinaryId));
			Assert.That(paired, Does.Not.Contain(takenOverId));
			Assert.That(_host.Services.GetRequiredService<IPluginTakeoverRegistry>().IsActive(takenOverId), Is.False);
		});
	}

	[Test]
	public async Task Developer_token_enrolment_of_an_installed_id_is_still_refused_as_installed()
	{
		var pluginId = InstallNewPlugin();
		var token = await Send(HttpMethod.Post,
			"/api/plugin-tokens",
			FakeConnectionShape.PublicLan,
			_adminToken,
			new { name = $"token-{Guid.NewGuid():N}", expiresInDays = (int?)null });
		var plaintext = (await ReadJson(token)).GetProperty("plaintext").GetString()!;

		var enrolment = await Send(HttpMethod.Post,
			"/api/plugins/registration",
			FakeConnectionShape.Loopback,
			body: new { pluginId, displayName = "Example Plugin" },
			extraHeaders: new Dictionary<string, string> { [PluginAuthDefaults.EnrollmentTokenHeaderName] = plaintext });
		var error = await ReadJson(enrolment);

		Assert.Multiple(() =>
		{
			Assert.That(enrolment.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
			Assert.That(error.GetProperty("details").GetProperty("reason").GetString(),
				Is.EqualTo(ProtocolErrorReasons.PluginInstalled));
		});
	}

	private string InstallNewPlugin()
	{
		var pluginId = NewPluginId();
		var version = new InstalledPluginVersion
		{
			Version = "1.0.0",
			VersionDirectory = Path.Combine(_dataDir, "fake-plugins", pluginId, "versions", "1.0.0"),
			ManifestPath = Path.Combine(_dataDir, "fake-plugins", pluginId, "versions", "1.0.0", "manifest.json")
		};
		_catalog.Plugins.Add(new InstalledPlugin
		{
			PluginId = pluginId,
			PluginDirectory = Path.Combine(_dataDir, "fake-plugins", pluginId),
			Versions = [version],
			ActiveVersion = version
		});

		return pluginId;
	}

	private async Task<(string PluginId, string Secret)> TakeOverAsync()
	{
		var pluginId = InstallNewPlugin();
		return (pluginId, await PairAsync(pluginId, takeOverInstalledPlugin: true));
	}

	private async Task<string> PairAsync(string pluginId, bool takeOverInstalledPlugin)
	{
		var (verifier, challenge) = NewPkcePair();
		var created = await CreatePairingRequestAsync(pluginId, challenge);
		Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created));
		var requestId = (await ReadJson(created)).GetProperty("requestId").GetString()!;

		var approve = await ApproveAsync(requestId, takeOverInstalledPlugin);
		Assert.That((await ReadJson(approve)).GetProperty("success").GetBoolean(), Is.True);

		var redemption = await RedeemAsync(requestId, verifier);
		Assert.That(redemption.StatusCode, Is.EqualTo(HttpStatusCode.Created));
		return (await ReadJson(redemption)).GetProperty("pluginSecret").GetString()!;
	}

	private List<PluginSessionSnapshot> SessionsOf(string pluginId)
		=> _host.Services.GetRequiredService<IPluginSessionRegistry>().Snapshot()
			.Where(session => session.PluginId == pluginId)
			.ToList();

	private Task<HttpResponseMessage> CreatePairingRequestAsync(string pluginId, string challenge)
		=> Send(HttpMethod.Post,
			"/api/plugins/pairing",
			FakeConnectionShape.Loopback,
			body: new
			{
				pluginId, displayName = "Development Build", codeChallenge = challenge, codeChallengeMethod = "S256"
			});

	private Task<HttpResponseMessage> ApproveAsync(string requestId, bool takeOverInstalledPlugin)
		=> Send(HttpMethod.Post,
			$"/api/plugin-pairing/requests/{requestId}/approve",
			FakeConnectionShape.Loopback,
			body: new { replaceExistingRegistration = false, takeOverInstalledPlugin });

	private Task<HttpResponseMessage> RedeemAsync(string requestId, string verifier)
		=> Send(HttpMethod.Post,
			$"/api/plugins/pairing/{requestId}/redemption",
			FakeConnectionShape.Loopback,
			body: new { codeVerifier = verifier });

	private async Task<JsonElement> PendingItemAsync(string requestId)
	{
		var response = await Send(HttpMethod.Get, "/api/plugin-pairing/requests", FakeConnectionShape.Loopback);
		return (await ReadJson(response)).GetProperty("requests").EnumerateArray()
			.Single(item => item.GetProperty("requestId").GetString() == requestId);
	}

	private async Task<JsonElement> PairedRegistrationAsync(string pluginId)
	{
		var response = await Send(HttpMethod.Get, "/api/plugin-pairing/registrations", FakeConnectionShape.Loopback);
		return (await ReadJson(response)).GetProperty("registrations").EnumerateArray()
			.Single(item => item.GetProperty("pluginId").GetString() == pluginId);
	}

	private async Task<IReadOnlyList<string>> PairedRegistrationIdsAsync()
	{
		var response = await Send(HttpMethod.Get, "/api/plugin-pairing/registrations", FakeConnectionShape.Loopback);
		return (await ReadJson(response)).GetProperty("registrations").EnumerateArray()
			.Select(item => item.GetProperty("pluginId").GetString()!)
			.ToList();
	}

	private async Task<JsonElement> RuntimeSnapshotAsync(string pluginId)
	{
		var response = await Send(HttpMethod.Get, "/api/plugin-runtime", FakeConnectionShape.PublicLan, _adminToken);
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		return (await ReadJson(response)).GetProperty("plugins").EnumerateArray()
			.Single(item => item.GetProperty("pluginId").GetString() == pluginId);
	}

	private Task<HttpResponseMessage> CreateSessionAsync(string pluginId, string secret)
		=> Send(HttpMethod.Post,
			"/api/plugins/sessions",
			FakeConnectionShape.Loopback,
			body: new { requestedVersion = new { minimum = 1, maximum = 1 }, capabilities = Array.Empty<object>() },
			extraHeaders: new Dictionary<string, string>
			{
				[PluginAuthDefaults.PluginIdHeaderName] = pluginId, [PluginAuthDefaults.PluginSecretHeaderName] = secret
			});

	private async Task SetDeveloperMode(bool enabled)
	{
		var response = await Send(HttpMethod.Put,
			"/api/settings/developer",
			FakeConnectionShape.PublicLan,
			_adminToken,
			new { enabled });
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
	}

	private static string NewPluginId() => $"com.example.t{Guid.NewGuid():N}";

	private static (string Verifier, string Challenge) NewPkcePair()
	{
		var verifier = $"verifier-{Guid.NewGuid():N}-{Guid.NewGuid():N}";
		var digest = SHA256.HashData(Encoding.UTF8.GetBytes(verifier));
		var challenge = Convert.ToBase64String(digest).TrimEnd('=').Replace('+', '-').Replace('/', '_');
		return (verifier, challenge);
	}

	private static StartupReadiness CompletedStartupReadiness()
	{
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();
		return readiness;
	}

	private async Task<HttpResponseMessage> Send(HttpMethod method,
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

	private sealed class SessionCreateHook
	{
		public Func<IServiceProvider, PluginSessionIdentity, Task>? BeforeCreate { get; set; }
	}

	// Runs the hook between the controller's admission check and the session insert, which is exactly
	// the window a concurrent revoke has to hit.
	private sealed class HookedSessionService : IPluginSessionService
	{
		private readonly IPluginSessionService _inner;
		private readonly IServiceProvider _provider;
		private readonly SessionCreateHook _hook;

		public HookedSessionService(IPluginSessionService inner, IServiceProvider provider, SessionCreateHook hook)
		{
			_inner = inner;
			_provider = provider;
			_hook = hook;
		}

		public async Task<PluginSessionCreationResult> Create(PluginSessionIdentity identity,
			PluginSessionRequest request)
		{
			if (_hook.BeforeCreate is { } before)
			{
				await before(_provider, identity);
			}

			return await _inner.Create(identity, request);
		}

		public Task<bool> Close(string sessionId, int closeCode, string reason)
			=> _inner.Close(sessionId, closeCode, reason);
	}
}
