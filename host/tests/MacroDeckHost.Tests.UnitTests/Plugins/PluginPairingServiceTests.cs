using System.Security.Cryptography;
using System.Text;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Compatibility;
using MacroDeckHost.Application.Plugins.Logging;
using MacroDeckHost.Application.Plugins.Pairing;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Tests.UnitTests.Auth;
using Microsoft.Extensions.Logging.Abstractions;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
public class PluginPairingServiceTests
{
	private sealed class FakeDeveloperModePreferenceService : IAppPreferenceService
	{
		public bool Enabled { get; set; } = true;

		public Task<DeveloperSettings> GetDeveloper() => Task.FromResult(new DeveloperSettings(Enabled));

		public Task<DeveloperSettings> SetDeveloper(bool? enabled)
		{
			Enabled = enabled ?? false;
			return Task.FromResult(new DeveloperSettings(Enabled));
		}

		public Task<AppearanceSettings> GetAppearance() => throw new NotSupportedException();

		public Task<AppearanceSettings> SetAppearance(string? themeMode, string? accentColor, string? fontFamily)
			=> throw new NotSupportedException();

		public Task<Guid> GetInstallationId() => throw new NotSupportedException();

		public Task<LoggingSettings> GetLogging() => throw new NotSupportedException();

		public Task<LoggingSettings> SetLogging(Application.Logging.LogEntryLevel? minimumLevel)
			=> throw new NotSupportedException();

		public Task<NetworkSettings> GetNetwork() => throw new NotSupportedException();

		public Task<NetworkSettings> SetNetwork(int? publicPort,
			bool? tlsEnabled = null,
			string? tlsMode = null,
			int? tlsHttpsPort = null,
			bool? discoveryEnabled = null)
			=> throw new NotSupportedException();

		public Task<AdbSettings> GetAdb() => throw new NotSupportedException();

		public Task<AdbSettings> SetAdb(bool? enabled,
			string? executablePath,
			bool? usbConnectionsEnabled,
			string? defaultDeviceSerial,
			bool? stopServerOnExit,
			bool allowPlugins)
			=> throw new NotSupportedException();

		public Task<OnboardingSettings> GetOnboarding() => throw new NotSupportedException();

		public Task<OnboardingSettings> SetOnboarding(bool? pending) => throw new NotSupportedException();

		public Task<LockScreenSettings> GetLockScreen() => throw new NotSupportedException();

		public Task<LockScreenSettings> SetLockScreen(bool? enabled) => throw new NotSupportedException();

		public Task<BackupSettings> GetBackups() => throw new NotSupportedException();

		public Task<BackupSettings> SetBackups(string? scheduleFrequency,
			string? scheduleTimeOfDay,
			string? scheduleDayOfWeek,
			int? scheduleDayOfMonth,
			string? retentionPolicy,
			int? retentionKeepLatest,
			bool? beforeHostUpdate,
			bool? beforePluginUpdate) => throw new NotSupportedException();

		public Task<DateTimeOffset?> GetBackupScheduleLastRun() => throw new NotSupportedException();

		public Task SetBackupScheduleLastRun(DateTimeOffset value) => throw new NotSupportedException();
		public Task<ExtensionSettings> GetExtensions() => throw new NotSupportedException();

		public Task<ExtensionSettings> SetExtensions(bool? storeEnabled,
			bool? checkForUpdates,
			bool? notifyOnUpdates,
			int? refreshIntervalMinutes,
		bool? autoUpdate = null)
			=> throw new NotSupportedException();

		public Task<LocalizationSettings> GetLocalization() => throw new NotSupportedException();

		public Task<LocalizationSettings> SetLocalization(string? culture) => throw new NotSupportedException();

		public Task<string> GetTimeFormat() => Task.FromResult(AppPreferenceService.TimeFormatSystem);

		public Task<string> SetTimeFormat(string? timeFormat) => throw new NotSupportedException();
	}

	private ManualTimeProvider _time = null!;
	private PluginPairingOptions _options = null!;
	private PluginPairingRequestStore _store = null!;
	private FakeDeveloperModePreferenceService _preferences = null!;
	private InMemoryPluginRegistrationRepository _registrations = null!;
	private InMemoryPluginAccessTokenRepository _tokens = null!;
	private PluginSessionRegistry _sessionRegistry = null!;
	private FakePluginInstallationCatalog _catalog = null!;
	private PluginRegistrationService _registrationService = null!;
	private PluginTakeoverRegistry _takeovers = null!;
	private FakePluginSupervisor _supervisor = null!;
	private PluginPairingService _service = null!;

	[SetUp]
	public void SetUp()
	{
		_time = new ManualTimeProvider();
		_options = new PluginPairingOptions
		{
			RequestLifetime = TimeSpan.FromMinutes(5), PollInterval = TimeSpan.FromSeconds(2), MaxPendingRequests = 5
		};
		_store = new PluginPairingRequestStore(_time, _options);
		_preferences = new FakeDeveloperModePreferenceService();
		_registrations = new InMemoryPluginRegistrationRepository();
		_tokens = new InMemoryPluginAccessTokenRepository();
		_sessionRegistry = new PluginSessionRegistry(_time, Serilog.Core.Logger.None);
		_catalog = new FakePluginInstallationCatalog();
		_takeovers = new PluginTakeoverRegistry();
		_supervisor = new FakePluginSupervisor();

		var logRateLimiter = new PluginLogRateLimiter(_time);
		var logIngestor = new PluginLogIngestor(logRateLimiter, _sessionRegistry, new FakePluginSupervisor(), _time);
		_registrationService = new PluginRegistrationService(_registrations,
			_tokens,
			_sessionRegistry,
			new PluginIdentityForgetter(new PluginCompatibilityService(NullLogger<PluginCompatibilityService>.Instance),
				logRateLimiter,
				logIngestor),
			_catalog,
			_takeovers,
			_time);

		_service = new PluginPairingService(_store,
			_preferences,
			_registrations,
			_registrationService,
			_catalog,
			_sessionRegistry,
			_supervisor,
			_takeovers);
	}

	private static (string Verifier, string Challenge) NewPkcePair()
	{
		var verifier = $"verifier-{Guid.NewGuid():N}-{Guid.NewGuid():N}";
		var digest = SHA256.HashData(Encoding.UTF8.GetBytes(verifier));
		var challenge = Convert.ToBase64String(digest).TrimEnd('=').Replace('+', '-').Replace('/', '_');
		return (verifier, challenge);
	}

	private async Task<(string RequestId, string Verifier)> CreateAndApprove(string pluginId,
		string displayName = "Example Plugin",
		bool replaceExistingRegistration = false,
		bool takeOverInstalledPlugin = false)
	{
		var (verifier, challenge) = NewPkcePair();

		var created = await _service.Create(pluginId,
			displayName,
			challenge,
			PluginPairingChallengeMethods.S256,
			client: null,
			arrivedOnPublicListener: false);
		Assert.That(created.Succeeded, Is.True, "precondition: create must succeed");

		var approved = await _service.Approve(created.Record!.RequestId,
			replaceExistingRegistration,
			takeOverInstalledPlugin);
		Assert.That(approved.Succeeded, Is.True, "precondition: approve must succeed");

		return (created.Record.RequestId, verifier);
	}

	[Test]
	public async Task Approve_then_redeem_yields_a_working_secret_and_exactly_one_registration_row()
	{
		var (requestId, verifier) = await CreateAndApprove("com.example.plugin");

		var redeemed = await _service.Redeem(requestId, verifier);

		Assert.Multiple(async () =>
		{
			Assert.That(redeemed.Succeeded, Is.True);
			Assert.That(redeemed.PluginId, Is.EqualTo("com.example.plugin"));
			var authenticated = await _registrationService.Authenticate("com.example.plugin", redeemed.PluginSecret!);
			Assert.That(authenticated, Is.Not.Null);
			Assert.That(_registrations.Registrations.Count(r => r.PluginId == "com.example.plugin"), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Redeeming_before_approval_fails_but_the_request_stays_redeemable_after_a_later_approval()
	{
		var (verifier, challenge) = NewPkcePair();
		var created = await _service.Create("com.example.plugin",
			"Example",
			challenge,
			PluginPairingChallengeMethods.S256,
			null,
			false);

		var tooEarly = await _service.Redeem(created.Record!.RequestId, verifier);
		Assert.That(tooEarly.Succeeded, Is.False);

		await _service.Approve(created.Record.RequestId, false);
		var redeemed = await _service.Redeem(created.Record.RequestId, verifier);

		Assert.That(redeemed.Succeeded, Is.True);
	}

	[Test]
	public async Task A_wrong_verifier_fails_without_consuming_the_request_and_the_correct_one_still_succeeds()
	{
		var (requestId, verifier) = await CreateAndApprove("com.example.plugin");

		var wrong = await _service.Redeem(requestId, "not-the-right-verifier");
		Assert.That(wrong.Succeeded, Is.False);

		var right = await _service.Redeem(requestId, verifier);
		Assert.That(right.Succeeded, Is.True);
	}

	[Test]
	public async Task A_second_redemption_after_success_fails_and_the_first_secret_still_authenticates()
	{
		var (requestId, verifier) = await CreateAndApprove("com.example.plugin");

		var first = await _service.Redeem(requestId, verifier);
		Assert.That(first.Succeeded, Is.True);

		var second = await _service.Redeem(requestId, verifier);

		Assert.Multiple(async () =>
		{
			Assert.That(second.Succeeded, Is.False);
			var authenticated
				= await _registrationService.Authenticate("com.example.plugin", first.PluginSecret!);
			Assert.That(authenticated, Is.Not.Null);
		});
	}

	// Not actually concurrent: every awaited dependency in this fixture completes synchronously, so
	// Task.WhenAll below runs the 16 redemptions to completion one after another rather than
	// interleaved. It still catches a missing compare-and-swap on the request's redeemed state - a
	// naive "check then set" would let more than one of these 16 calls succeed.
	[Test]
	public async Task Repeated_redemptions_of_one_approved_request_produce_exactly_one_success()
	{
		var (requestId, verifier) = await CreateAndApprove("com.example.plugin");

		var tasks = Enumerable.Range(0, 16).Select(_ => _service.Redeem(requestId, verifier)).ToArray();
		var results = await Task.WhenAll(tasks);

		Assert.Multiple(() =>
		{
			Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(1));
			Assert.That(_registrations.Registrations.Count(r => r.PluginId == "com.example.plugin"), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Past_the_ttl_status_reports_expired_and_approve_and_redeem_both_fail()
	{
		var (verifier, challenge) = NewPkcePair();
		var created = await _service.Create("com.example.plugin",
			"Example",
			challenge,
			PluginPairingChallengeMethods.S256,
			null,
			false);
		var requestId = created.Record!.RequestId;

		_time.Advance(_options.RequestLifetime + TimeSpan.FromSeconds(1));

		var status = _service.Status(requestId);
		var approve = await _service.Approve(requestId, false);
		var redeem = await _service.Redeem(requestId, verifier);

		Assert.Multiple(() =>
		{
			Assert.That(status.Status, Is.EqualTo(PluginPairingStatuses.Expired));
			Assert.That(approve.Succeeded, Is.False);
			Assert.That(redeem.Succeeded, Is.False);
			Assert.That(_registrations.Registrations, Is.Empty);
		});
	}

	[Test]
	public async Task After_reject_redeem_fails_no_registration_exists_and_status_reports_rejected()
	{
		var (verifier, challenge) = NewPkcePair();
		var created = await _service.Create("com.example.plugin",
			"Example",
			challenge,
			PluginPairingChallengeMethods.S256,
			null,
			false);
		var requestId = created.Record!.RequestId;

		Assert.That(_service.Reject(requestId), Is.True);

		var redeem = await _service.Redeem(requestId, verifier);
		var status = _service.Status(requestId);

		Assert.Multiple(() =>
		{
			Assert.That(redeem.Succeeded, Is.False);
			Assert.That(_registrations.Registrations, Is.Empty);
			Assert.That(status.Status, Is.EqualTo(PluginPairingStatuses.Rejected));
		});
	}

	[Test]
	public async Task A_create_for_a_plugin_id_with_a_live_request_is_refused_and_the_original_survives()
	{
		var (verifier, challenge) = NewPkcePair();
		var first = await _service.Create("com.example.plugin",
			"Example",
			challenge,
			PluginPairingChallengeMethods.S256,
			null,
			false);
		Assert.That(first.Succeeded, Is.True);

		var (_, secondChallenge) = NewPkcePair();
		var second = await _service.Create("com.example.plugin",
			"Example again",
			secondChallenge,
			PluginPairingChallengeMethods.S256,
			null,
			false);

		Assert.That(second.Succeeded, Is.False);
		Assert.That(second.Error, Is.EqualTo(PluginPairingCreateError.DuplicateRequest));

		await _service.Approve(first.Record!.RequestId, false);
		var redeemed = await _service.Redeem(first.Record.RequestId, verifier);
		Assert.That(redeemed.Succeeded, Is.True, "the original request must still be redeemable");
	}

	[Test]
	public async Task Creates_beyond_the_global_cap_are_refused_while_earlier_ones_stay_pending()
	{
		for (var i = 0; i < _options.MaxPendingRequests; i++)
		{
			var (_, challenge) = NewPkcePair();
			var result = await _service.Create($"com.example.plugin{i}",
				"Example",
				challenge,
				PluginPairingChallengeMethods.S256,
				null,
				false);
			Assert.That(result.Succeeded, Is.True, $"request {i} should be within the cap");
		}

		var (_, overCapChallenge) = NewPkcePair();
		var overCap = await _service.Create("com.example.pluginover",
			"Example",
			overCapChallenge,
			PluginPairingChallengeMethods.S256,
			null,
			false);

		Assert.That(overCap.Succeeded, Is.False);
		Assert.That(overCap.Error, Is.EqualTo(PluginPairingCreateError.CapacityExceeded));

		var pending = await _service.Pending();
		Assert.That(pending, Has.Count.EqualTo(_options.MaxPendingRequests));
	}

	[Test]
	public async Task Developer_mode_off_refuses_create_and_records_nothing()
	{
		_preferences.Enabled = false;
		var (_, challenge) = NewPkcePair();

		var result = await _service.Create("com.example.plugin",
			"Example",
			challenge,
			PluginPairingChallengeMethods.S256,
			null,
			false);

		Assert.Multiple(async () =>
		{
			Assert.That(result.Succeeded, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginPairingCreateError.DeveloperModeDisabled));
			Assert.That((await _service.Pending()), Is.Empty);
		});
	}

	[Test]
	public async Task Turning_developer_mode_off_after_approval_blocks_redemption()
	{
		var (requestId, verifier) = await CreateAndApprove("com.example.plugin");

		_preferences.Enabled = false;

		var redeemed = await _service.Redeem(requestId, verifier);

		Assert.Multiple(() =>
		{
			Assert.That(redeemed.Succeeded, Is.False);
			Assert.That(_registrations.Registrations, Is.Empty);
		});
	}

	private void Install(string pluginId)
		=> _catalog.Plugins.Add(new InstalledPlugin
		{
			PluginId = pluginId,
			PluginDirectory = $"/plugins/{pluginId}",
			Versions =
			[
				new InstalledPluginVersion
				{
					Version = "1.0.0",
					VersionDirectory = $"/plugins/{pluginId}/versions/1.0.0",
					ManifestPath = $"/plugins/{pluginId}/versions/1.0.0/manifest.json"
				}
			]
		});

	private bool HasActiveRegistration(string pluginId)
		=> _registrations.Registrations.Any(r => r.PluginId == pluginId && r.RevokedAt is null);

	[Test]
	public async Task Pairing_an_installed_id_creates_a_request_marked_as_a_takeover()
	{
		Install("com.example.installed");
		var (_, challenge) = NewPkcePair();

		var result = await _service.Create("com.example.installed",
			"Example",
			challenge,
			PluginPairingChallengeMethods.S256,
			null,
			false);
		var pending = await _service.Pending();

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.True);
			Assert.That(result.TakesOverInstalledPlugin, Is.True);
			Assert.That(pending.Single().TakesOverInstalledPlugin, Is.True);
			Assert.That(pending.Single().ReplacesExistingRegistration, Is.False);
		});
	}

	[Test]
	public async Task A_takeover_is_only_approved_with_its_own_confirmation_and_approval_alone_changes_nothing()
	{
		Install("com.example.installed");
		var (_, challenge) = NewPkcePair();
		var created = await _service.Create("com.example.installed",
			"Example",
			challenge,
			PluginPairingChallengeMethods.S256,
			null,
			false);

		var unconfirmed = await _service.Approve(created.Record!.RequestId, replaceExistingRegistration: false);
		var confirmed = await _service.Approve(created.Record.RequestId,
			replaceExistingRegistration: false,
			takeOverInstalledPlugin: true);

		Assert.Multiple(() =>
		{
			Assert.That(unconfirmed.Succeeded, Is.False);
			Assert.That(unconfirmed.Error, Is.EqualTo(PluginPairingApproveError.TakeoverNotConfirmed));
			Assert.That(confirmed.Succeeded, Is.True);
			Assert.That(_registrations.Registrations, Is.Empty);
			Assert.That(_takeovers.IsActive("com.example.installed"), Is.False);
			Assert.That(_supervisor.SuspendCalls, Is.Empty);
		});
	}

	[Test]
	public async Task Redeeming_a_confirmed_takeover_suspends_the_installed_plugin_and_issues_a_working_credential()
	{
		Install("com.example.installed");
		var (requestId, verifier) = await CreateAndApprove("com.example.installed", takeOverInstalledPlugin: true);

		var redeemed = await _service.Redeem(requestId, verifier);

		var authenticated = await _registrationService.Authenticate("com.example.installed", redeemed.PluginSecret!);
		var paired = await _service.PairedRegistrations();
		Assert.Multiple(() =>
		{
			Assert.That(redeemed.Succeeded, Is.True);
			Assert.That(authenticated, Is.Not.Null);
			Assert.That(_supervisor.SuspendCalls, Is.EqualTo(new[] { "com.example.installed" }));
			Assert.That(_takeovers.IsActive("com.example.installed"), Is.True);
			Assert.That(paired.Single().TakesOverInstalledPlugin, Is.True);
		});
	}

	[Test]
	public async Task Developer_mode_switched_off_while_a_takeover_is_redeemed_leaves_no_credential_and_no_takeover()
	{
		Install("com.example.installed");
		var (requestId, verifier) = await CreateAndApprove("com.example.installed", takeOverInstalledPlugin: true);
		_supervisor.OnSuspend = _ =>
		{
			_preferences.Enabled = false;
			return Task.CompletedTask;
		};

		var redeemed = await _service.Redeem(requestId, verifier);

		Assert.Multiple(() =>
		{
			Assert.That(redeemed.Succeeded, Is.False);
			Assert.That(HasActiveRegistration("com.example.installed"), Is.False);
			Assert.That(_takeovers.IsActive("com.example.installed"), Is.False);
		});
	}

	[Test]
	public async Task A_takeover_ended_while_it_is_redeemed_leaves_no_credential_behind()
	{
		Install("com.example.installed");
		var (requestId, verifier) = await CreateAndApprove("com.example.installed", takeOverInstalledPlugin: true);
		_supervisor.OnSuspend = pluginId =>
		{
			_takeovers.Finish(pluginId);
			return Task.CompletedTask;
		};

		var redeemed = await _service.Redeem(requestId, verifier);

		Assert.Multiple(() =>
		{
			Assert.That(redeemed.Succeeded, Is.False);
			Assert.That(HasActiveRegistration("com.example.installed"), Is.False);
			Assert.That(_takeovers.IsActive("com.example.installed"), Is.False);
		});
	}

	[Test]
	public async Task A_request_approved_before_the_id_was_installed_does_not_take_the_installed_plugin_over()
	{
		var (requestId, verifier) = await CreateAndApprove("com.example.late");
		Install("com.example.late");

		var redeemed = await _service.Redeem(requestId, verifier);

		Assert.Multiple(() =>
		{
			Assert.That(redeemed.Succeeded, Is.False);
			Assert.That(_registrations.Registrations, Is.Empty);
			Assert.That(_supervisor.SuspendCalls, Is.Empty);
			Assert.That(_takeovers.IsActive("com.example.late"), Is.False);
		});
	}

	[Test]
	public async Task A_second_pairing_during_a_takeover_needs_both_confirmations_and_keeps_the_same_takeover()
	{
		Install("com.example.installed");
		var (firstRequestId, firstVerifier) = await CreateAndApprove("com.example.installed",
			takeOverInstalledPlugin: true);
		var first = await _service.Redeem(firstRequestId, firstVerifier);
		var ticket = _takeovers.CurrentTicket("com.example.installed");

		var (verifier, challenge) = NewPkcePair();
		var created = await _service.Create("com.example.installed",
			"Example again",
			challenge,
			PluginPairingChallengeMethods.S256,
			null,
			false);
		var pending = (await _service.Pending()).Single();
		var withoutReplacement = await _service.Approve(created.Record!.RequestId,
			replaceExistingRegistration: false,
			takeOverInstalledPlugin: true);
		var withoutTakeover = await _service.Approve(created.Record.RequestId,
			replaceExistingRegistration: true,
			takeOverInstalledPlugin: false);
		var withBoth = await _service.Approve(created.Record.RequestId,
			replaceExistingRegistration: true,
			takeOverInstalledPlugin: true);
		var second = await _service.Redeem(created.Record.RequestId, verifier);

		var oldAuthenticated = await _registrationService.Authenticate("com.example.installed", first.PluginSecret!);
		var newAuthenticated = await _registrationService.Authenticate("com.example.installed", second.PluginSecret!);
		Assert.Multiple(() =>
		{
			Assert.That(pending.TakesOverInstalledPlugin, Is.True);
			Assert.That(pending.ReplacesExistingRegistration, Is.True);
			Assert.That(withoutReplacement.Error, Is.EqualTo(PluginPairingApproveError.ReplacementNotConfirmed));
			Assert.That(withoutTakeover.Error, Is.EqualTo(PluginPairingApproveError.TakeoverNotConfirmed));
			Assert.That(withBoth.Succeeded, Is.True);
			Assert.That(second.Succeeded, Is.True);
			Assert.That(oldAuthenticated, Is.Null);
			Assert.That(newAuthenticated, Is.Not.Null);
			Assert.That(_takeovers.CurrentTicket("com.example.installed"), Is.EqualTo(ticket));
			Assert.That(_supervisor.SuspendCalls, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task Replacing_a_credential_left_on_an_installed_id_without_a_takeover_starts_a_takeover_first()
	{
		Install("com.example.installed");
		await _registrationService.Register("com.example.installed",
			"Left over",
			accessTokenId: null,
			PluginRegistrationOrigins.Pairing,
			allowInstalledId: true);

		var (requestId, verifier) = await CreateAndApprove("com.example.installed",
			replaceExistingRegistration: true,
			takeOverInstalledPlugin: true);
		var redeemed = await _service.Redeem(requestId, verifier);

		Assert.Multiple(() =>
		{
			Assert.That(redeemed.Succeeded, Is.True);
			Assert.That(_supervisor.SuspendCalls, Is.EqualTo(new[] { "com.example.installed" }));
			Assert.That(_takeovers.IsActive("com.example.installed"), Is.True);
		});
	}

	[Test]
	public async Task Revoking_a_takeover_credential_ends_the_takeover()
	{
		Install("com.example.installed");
		var (requestId, verifier) = await CreateAndApprove("com.example.installed", takeOverInstalledPlugin: true);
		await _service.Redeem(requestId, verifier);

		await _registrationService.Revoke("com.example.installed");

		Assert.Multiple(async () =>
		{
			Assert.That(_takeovers.IsActive("com.example.installed"), Is.False);
			Assert.That(HasActiveRegistration("com.example.installed"), Is.False);
			Assert.That((await _service.PairedRegistrations()), Is.Empty);
		});
	}

	[Test]
	public async Task Replacement_requires_explicit_confirmation_and_then_retires_the_old_secret()
	{
		var originalRegistration = await _registrationService.Register("com.example.plugin",
			"Original",
			accessTokenId: null,
			PluginRegistrationOrigins.Pairing);

		var connection = new FakePluginConnection();
		await _sessionRegistry.Create(new PluginSessionRecord
		{
			SessionId = "session-1",
			PluginId = "com.example.plugin",
			DisplayName = "Original",
			AccessTokenId = null,
			Origin = PluginSessionOrigin.SelfRegistered,
			NegotiatedVersion = 1,
			Capabilities = new Dictionary<string, MacroDeck.Plugin.Protocol.Versioning.CapabilityNegotiationResult>(),
			DeclaredCapabilities = [],
			State = PluginSessionState.Connected,
			CreatedAt = _time.GetUtcNow()
		});
		_sessionRegistry.TryAttach("session-1", connection, instanceId: null);

		var (verifier, challenge) = NewPkcePair();
		var created = await _service.Create("com.example.plugin",
			"Replacement",
			challenge,
			PluginPairingChallengeMethods.S256,
			null,
			false);
		Assert.That(created.Succeeded, Is.True);

		var refusedApproval = await _service.Approve(created.Record!.RequestId, replaceExistingRegistration: false);
		Assert.That(refusedApproval.Succeeded, Is.False);
		Assert.That(refusedApproval.Error, Is.EqualTo(PluginPairingApproveError.ReplacementNotConfirmed));

		var confirmedApproval = await _service.Approve(created.Record.RequestId, replaceExistingRegistration: true);
		Assert.That(confirmedApproval.Succeeded, Is.True);

		var redeemed = await _service.Redeem(created.Record.RequestId, verifier);

		Assert.Multiple(async () =>
		{
			Assert.That(redeemed.Succeeded, Is.True);
			var oldAuthenticated = await _registrationService.Authenticate("com.example.plugin",
				originalRegistration.PluginSecret!);
			Assert.That(oldAuthenticated, Is.Null);
			var newAuthenticated
				= await _registrationService.Authenticate("com.example.plugin", redeemed.PluginSecret!);
			Assert.That(newAuthenticated, Is.Not.Null);
			Assert.That(
				_registrations.Registrations.Count(r => r.PluginId == "com.example.plugin" && r.RevokedAt is null),
				Is.EqualTo(1));
			Assert.That(connection.Closes, Is.Not.Empty, "the plugin's live session must be terminated");
		});
	}

	[Test]
	public async Task Approval_alone_mutates_nothing_the_old_secret_survives_an_unredeemed_expired_replacement()
	{
		var originalRegistration = await _registrationService.Register("com.example.plugin",
			"Original",
			accessTokenId: null,
			PluginRegistrationOrigins.Pairing);

		await CreateAndApprove("com.example.plugin", "Replacement", true);

		_time.Advance(_options.RequestLifetime + TimeSpan.FromSeconds(1));

		var oldAuthenticated
			= await _registrationService.Authenticate("com.example.plugin", originalRegistration.PluginSecret!);

		Assert.Multiple(() =>
		{
			Assert.That(oldAuthenticated, Is.Not.Null);
			var registration = _registrations.Registrations.Single(r => r.PluginId == "com.example.plugin");
			Assert.That(registration.RevokedAt, Is.Null);
		});
	}
}
