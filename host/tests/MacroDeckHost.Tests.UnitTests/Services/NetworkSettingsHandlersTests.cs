using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Lifecycle;
using MacroDeckHost.Application.Network.Discovery;
using MacroDeckHost.Application.Network.Tls;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Services;

public class NetworkSettingsHandlersTests
{
	private const int ActivePort = BuildConfig.DefaultPublicPort;
	private const int LoopbackPort = 51234;
	private const string LoopbackPortValue = "51234";

	private sealed class CountingAppPreferenceRepository : IAppPreferenceRepository
	{
		private readonly Dictionary<string, AppPreferenceEntity> _store = new();

		public int Writes { get; private set; }

		private int _changeReads;

		public (string Key, int OnRead, string Value)? ChangeBehindTheHandler { get; set; }

		public Task<AppPreferenceEntity?> GetByKey(string key)
		{
			if (ChangeBehindTheHandler is { } change && change.Key == key && ++_changeReads == change.OnRead)
			{
				_store[key] = new AppPreferenceEntity { Key = key, Value = change.Value };
			}

			return Task.FromResult(_store.GetValueOrDefault(key));
		}

		public Task SetValue(string key, string value)
		{
			Writes++;
			_store[key] = new AppPreferenceEntity { Key = key, Value = value };
			return Task.CompletedTask;
		}

		public void Seed(string key, string value)
			=> _store[key] = new AppPreferenceEntity { Key = key, Value = value };
	}

	private sealed class FakeBuildEnvironment : IBuildEnvironment
	{
		public string Version => "0.0.0-test";

		public bool IsBeta => false;

		public BuildChannel Channel => BuildChannel.Production;
	}

	private sealed class FakeRestartService : IApplicationRestartService
	{
		public RestartAvailability Availability { get; init; } = new(true, null);

		public bool RestartRequested => false;

		public Result<RestartError> Request(string reason) => Result.Ok<RestartError>();
	}

	private sealed class FakeLocalAddressProvider : ILocalAddressProvider
	{
		public IReadOnlyList<IPAddress> GetReachableIpv4Addresses() => [];
	}

	private sealed class FakeHostNameProvider : IHostNameProvider
	{
		public string MachineName => "deck-pc";

		public IReadOnlyList<string> GetHostNames() => ["deck-pc", "deck-pc.local"];
	}

	private sealed class FakeDiscoveryRefresher : IDiscoveryAdvertisementRefresher
	{
		public int Requests { get; private set; }

		public void RequestRefresh() => Requests++;
	}

	private sealed record Fixture(
		CountingAppPreferenceRepository Repository,
		FakePublicTlsCertificateStore CertificateStore,
		UserNotificationStore Notifications,
		GetNetworkSettingsRequestMessageHandler Get,
		UpdateNetworkSettingsRequestMessageHandler Update,
		UpdateNetworkTlsCertificateRequestMessageHandler UpdateCertificate,
		ReissueTlsCertificateRequestMessageHandler ReissueCertificate,
		FakeDiscoveryRefresher Discovery);

	private static Fixture CreateFixture(
		bool overriddenByEnvironment = false,
		RestartAvailability? restart = null,
		int? loopbackPort = LoopbackPort,
		int? refusedConfiguredPort = null,
		bool publicListenerAvailable = true,
		PublicTlsMode activeTlsMode = PublicTlsMode.Additional,
		int? activeTlsHttpsPort = BuildConfig.DefaultPublicHttpsPort,
		string? activeCertificateFingerprint = null)
	{
		var repository = new CountingAppPreferenceRepository();
		var certificateStore = new FakePublicTlsCertificateStore();
		var publicEndpoints = !publicListenerAvailable
			? PublicEndpointSet.HttpOnly(ActivePort).WithoutHttp()
			: activeTlsMode switch
			{
				PublicTlsMode.Replace => PublicEndpointSet.HttpsReplacingHttp(ActivePort),
				PublicTlsMode.Additional => PublicEndpointSet.HttpAndHttps(ActivePort, activeTlsHttpsPort!.Value),
				_ => PublicEndpointSet.HttpOnly(ActivePort)
			};

		var listenerState = new FakeHostListenerState
		{
			PublicPort = ActivePort,
			PublicPortOverriddenByEnvironment = overriddenByEnvironment,
			RefusedConfiguredPublicPort = refusedConfiguredPort,
			LoopbackPort = loopbackPort,
			PublicListenerAvailable = publicListenerAvailable,
			PublicEndpoints = publicEndpoints,
			ActiveCertificateFingerprint = activeCertificateFingerprint
		};
		var service = new AppPreferenceService(repository, new FakeBuildEnvironment(), listenerState, certificateStore);
		var restartService = new FakeRestartService { Availability = restart ?? new RestartAvailability(true, null) };

		var notifications = new UserNotificationStore();
		var notifier = new NetworkRestartNotifier(service, restartService, notifications, TestLocalization.Resolver);
		var addressProvider = new FakeLocalAddressProvider();
		var discovery = new FakeDiscoveryRefresher();

		return new Fixture(repository,
			certificateStore,
			notifications,
			new GetNetworkSettingsRequestMessageHandler(service, restartService),
			new UpdateNetworkSettingsRequestMessageHandler(service, restartService, listenerState, notifier, discovery),
			new UpdateNetworkTlsCertificateRequestMessageHandler(service, restartService, certificateStore, notifier),
			new ReissueTlsCertificateRequestMessageHandler(service,
				restartService,
				new PublicTlsBootstrapper(certificateStore,
					addressProvider,
					new FakeHostNameProvider(),
					new LoggerConfiguration().CreateLogger()),
				notifier),
			discovery);
	}

	private static (string CertificatePem, string PrivateKeyPem, string Fingerprint) ReissueCertificate()
	{
		using var rsa = RSA.Create(2048);
		var request = new CertificateRequest("CN=uploaded.macrodeck",
			rsa,
			HashAlgorithmName.SHA256,
			RSASignaturePadding.Pkcs1);
		var sanBuilder = new SubjectAlternativeNameBuilder();
		sanBuilder.AddDnsName("localhost");
		sanBuilder.AddIpAddress(IPAddress.Loopback);
		request.CertificateExtensions.Add(sanBuilder.Build());

		using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
			DateTimeOffset.UtcNow.AddYears(1));

		return (certificate.ExportCertificatePem(),
			rsa.ExportPkcs8PrivateKeyPem(),
			certificate.GetCertHashString(HashAlgorithmName.SHA256));
	}

	[Test]
	public async Task Discovery_is_on_for_a_fresh_installation()
	{
		var fixture = CreateFixture();

		var response = await fixture.Get.Handle(new GetNetworkSettingsRequest(), CancellationToken.None);

		Assert.That(response.DiscoveryEnabled, Is.True);
	}

	[Test]
	public async Task Turning_discovery_off_is_saved_live_without_asking_for_a_restart()
	{
		var fixture = CreateFixture();

		var response = await fixture.Update.Handle(
			new UpdateNetworkSettingsRequest { PublicPort = ActivePort, DiscoveryEnabled = false },
			CancellationToken.None);
		var stored = await fixture.Repository.GetByKey(AppPreferenceService.DiscoveryEnabledKey);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(response.DiscoveryEnabled, Is.False);
			Assert.That(response.RestartRequired, Is.False);
			Assert.That(stored?.Value, Is.EqualTo(bool.FalseString));
			Assert.That(fixture.Discovery.Requests, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task A_save_that_omits_discovery_keeps_the_stored_value()
	{
		var fixture = CreateFixture();
		fixture.Repository.Seed(AppPreferenceService.DiscoveryEnabledKey, bool.FalseString);

		var response = await fixture.Update.Handle(new UpdateNetworkSettingsRequest { PublicPort = 9100 },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.PublicPort, Is.EqualTo(9100));
			Assert.That(response.DiscoveryEnabled, Is.False);
		});
	}

	[Test]
	public async Task A_tls_save_does_not_revert_a_discovery_change_that_landed_after_the_handler_read()
	{
		var fixture = CreateFixture();
		fixture.Repository.ChangeBehindTheHandler = (AppPreferenceService.DiscoveryEnabledKey, 2, bool.FalseString);

		var response = await fixture.Update.Handle(
			new UpdateNetworkSettingsRequest { PublicPort = ActivePort, TlsHttpsPort = 9443 },
			CancellationToken.None);
		var stored = await fixture.Repository.GetByKey(AppPreferenceService.DiscoveryEnabledKey);

		Assert.Multiple(() =>
		{
			Assert.That(response.TlsHttpsPort, Is.EqualTo(9443));
			Assert.That(response.DiscoveryEnabled, Is.False);
			Assert.That(stored?.Value, Is.EqualTo(bool.FalseString));
		});
	}

	[Test]
	public async Task Get_reports_the_default_port_for_a_fresh_installation()
	{
		var fixture = CreateFixture();

		var response = await fixture.Get.Handle(new GetNetworkSettingsRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.PublicPort, Is.EqualTo(BuildConfig.DefaultPublicPort));
			Assert.That(response.DefaultPublicPort, Is.EqualTo(BuildConfig.DefaultPublicPort));
			Assert.That(response.ActivePublicPort, Is.EqualTo(ActivePort));
			Assert.That(response.MinimumPublicPort, Is.EqualTo(PublicPortSelector.MinimumConfigurablePort));
			Assert.That(response.MaximumPublicPort, Is.EqualTo(PublicPortSelector.MaximumConfigurablePort));
			Assert.That(response.RestartRequired, Is.False);
			Assert.That(response.RestartSupported, Is.True);
		});
	}

	[Test]
	public async Task A_stored_port_that_is_not_active_yet_asks_for_a_restart()
	{
		var fixture = CreateFixture();
		fixture.Repository.Seed(AppPreferenceService.PublicPortKey, "9100");

		var response = await fixture.Get.Handle(new GetNetworkSettingsRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.PublicPort, Is.EqualTo(9100));
			Assert.That(response.RestartRequired, Is.True);
		});
	}

	[Test]
	public async Task An_environment_override_suppresses_the_restart_hint()
	{
		var fixture = CreateFixture(overriddenByEnvironment: true);
		fixture.Repository.Seed(AppPreferenceService.PublicPortKey, "9100");

		var response = await fixture.Get.Handle(new GetNetworkSettingsRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.OverriddenByEnvironment, Is.True);
			Assert.That(response.RestartRequired, Is.False);
		});
	}

	// A restart would refuse the value again, so promising one would be a lie the user cannot escape.
	[Test]
	public async Task A_stored_port_the_last_start_refused_does_not_ask_for_a_restart()
	{
		var fixture = CreateFixture(refusedConfiguredPort: LoopbackPort);
		fixture.Repository.Seed(AppPreferenceService.PublicPortKey, LoopbackPortValue);

		var response = await fixture.Get.Handle(new GetNetworkSettingsRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.PublicPort, Is.EqualTo(LoopbackPort));
			Assert.That(response.ConfiguredPortIgnored, Is.True);
			Assert.That(response.RestartRequired, Is.False);
		});
	}

	[Test]
	public async Task Saving_a_usable_port_clears_the_refused_state()
	{
		var fixture = CreateFixture(refusedConfiguredPort: LoopbackPort);
		fixture.Repository.Seed(AppPreferenceService.PublicPortKey, LoopbackPortValue);

		var response = await fixture.Update.Handle(new UpdateNetworkSettingsRequest { PublicPort = 9100 },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(response.ConfiguredPortIgnored, Is.False);
			Assert.That(response.RestartRequired, Is.True);
		});
	}

	[Test]
	public async Task Restart_support_follows_the_restart_service()
	{
		var fixture = CreateFixture(restart: new RestartAvailability(false, "no shell"));

		var response = await fixture.Get.Handle(new GetNetworkSettingsRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.RestartSupported, Is.False);
			Assert.That(response.RestartUnsupportedReason, Is.EqualTo("no shell"));
		});
	}

	[TestCase(0)]
	[TestCase(80)]
	[TestCase(1023)]
	[TestCase(70000)]
	public async Task An_invalid_port_is_rejected_without_writing_anything(int port)
	{
		var fixture = CreateFixture();

		var response = await fixture.Update.Handle(new UpdateNetworkSettingsRequest { PublicPort = port },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error, Is.Not.Empty);
			Assert.That(response.PublicPort, Is.EqualTo(BuildConfig.DefaultPublicPort));
			Assert.That(response.RestartRequired, Is.False);
			Assert.That(fixture.Repository.Writes, Is.Zero);
		});
	}

	[Test]
	public async Task The_loopback_port_is_rejected_without_writing_anything()
	{
		var fixture = CreateFixture();

		var response = await fixture.Update.Handle(new UpdateNetworkSettingsRequest { PublicPort = LoopbackPort },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error, Is.Not.Empty);
			Assert.That(fixture.Repository.Writes, Is.Zero);
		});
	}

	[Test]
	public async Task An_unchanged_port_is_not_written_again()
	{
		var fixture = CreateFixture();
		fixture.Repository.Seed(AppPreferenceService.PublicPortKey, "9100");

		var response = await fixture.Update.Handle(new UpdateNetworkSettingsRequest { PublicPort = 9100 },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(response.PublicPort, Is.EqualTo(9100));
			Assert.That(fixture.Repository.Writes, Is.Zero);
		});
	}

	[Test]
	public async Task A_changed_port_is_persisted_and_asks_for_a_restart()
	{
		var fixture = CreateFixture();

		var response = await fixture.Update.Handle(new UpdateNetworkSettingsRequest { PublicPort = 9100 },
			CancellationToken.None);
		var reloaded = await fixture.Get.Handle(new GetNetworkSettingsRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(response.Error, Is.Null);
			Assert.That(response.PublicPort, Is.EqualTo(9100));
			Assert.That(response.RestartRequired, Is.True);
			Assert.That(fixture.Repository.Writes, Is.EqualTo(1));
			Assert.That(reloaded.PublicPort, Is.EqualTo(9100));
		});
	}

	[Test]
	public async Task A_changed_port_raises_a_notification_with_a_restart_button()
	{
		var fixture = CreateFixture();

		await fixture.Update.Handle(new UpdateNetworkSettingsRequest { PublicPort = 9100 },
			CancellationToken.None);

		var notification = fixture.Notifications.Snapshot().Single();
		Assert.Multiple(() =>
		{
			Assert.That(notification.Message, Is.Not.Empty);
			Assert.That(notification.Action!.Kind, Is.EqualTo(UserNotificationActionKind.RestartApplication));
		});
	}

	[Test]
	public async Task A_rejected_port_raises_no_notification()
	{
		var fixture = CreateFixture();

		await fixture.Update.Handle(new UpdateNetworkSettingsRequest { PublicPort = 80 },
			CancellationToken.None);

		Assert.That(fixture.Notifications.Snapshot(), Is.Empty);
	}

	[Test]
	public async Task Returning_to_the_active_port_is_written_and_clears_the_restart_hint()
	{
		var fixture = CreateFixture();
		fixture.Repository.Seed(AppPreferenceService.PublicPortKey, "9100");

		var response = await fixture.Update.Handle(new UpdateNetworkSettingsRequest { PublicPort = ActivePort },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(response.PublicPort, Is.EqualTo(ActivePort));
			Assert.That(response.RestartRequired, Is.False);
			Assert.That(fixture.Repository.Writes, Is.EqualTo(1));
		});
	}

	// Both handlers build their response with a separate object initialiser (issue #515); the flag has
	// to be threaded through both, and reported the same way regardless of which one is asked.
	[Test]
	public async Task Both_handlers_report_the_public_listener_flag_in_both_directions()
	{
		var available = CreateFixture(publicListenerAvailable: true);
		var unavailable = CreateFixture(publicListenerAvailable: false);

		var availableGet = await available.Get.Handle(new GetNetworkSettingsRequest(), CancellationToken.None);
		var unavailableGet = await unavailable.Get.Handle(new GetNetworkSettingsRequest(), CancellationToken.None);
		var availableUpdate = await available.Update.Handle(
			new UpdateNetworkSettingsRequest { PublicPort = ActivePort },
			CancellationToken.None);
		var unavailableUpdate = await unavailable.Update.Handle(
			new UpdateNetworkSettingsRequest { PublicPort = ActivePort },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(availableGet.PublicListenerUnavailable, Is.False);
			Assert.That(unavailableGet.PublicListenerUnavailable, Is.True);
			Assert.That(availableUpdate.PublicListenerUnavailable, Is.False);
			Assert.That(unavailableUpdate.PublicListenerUnavailable, Is.True);
			Assert.That(availableGet.ActivePublicPort, Is.EqualTo(ActivePort));
			Assert.That(unavailableGet.ActivePublicPort, Is.EqualTo(ActivePort));
		});
	}

	// A user who frees the held port in another app (or fixes the Windows-reserved-range problem)
	// needs a way to retry the bind, even though the configured port never changed.
	[Test]
	public async Task Restart_is_offered_while_the_listener_is_unavailable_even_with_no_pending_port_change()
	{
		var unavailable = CreateFixture(publicListenerAvailable: false);
		var available = CreateFixture(publicListenerAvailable: true);

		var unavailableResponse = await unavailable.Get.Handle(new GetNetworkSettingsRequest(), CancellationToken.None);
		var availableResponse = await available.Get.Handle(new GetNetworkSettingsRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(unavailableResponse.PublicPort, Is.EqualTo(unavailableResponse.ActivePublicPort));
			Assert.That(unavailableResponse.RestartRequired, Is.True);
			Assert.That(availableResponse.PublicPort, Is.EqualTo(availableResponse.ActivePublicPort));
			Assert.That(availableResponse.RestartRequired, Is.False);
		});
	}

	// Restarting would re-apply the same MACRO_DECK_PORT and refuse it again, so the restart hint would
	// be a promise the host cannot keep.
	[Test]
	public async Task Restart_is_not_offered_while_unavailable_under_an_environment_override()
	{
		var fixture = CreateFixture(overriddenByEnvironment: true, publicListenerAvailable: false);

		var response = await fixture.Get.Handle(new GetNetworkSettingsRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.RestartRequired, Is.False);
			Assert.That(response.PublicListenerUnavailable, Is.True);
		});
	}

	[Test]
	public async Task The_update_handler_reports_the_flag_and_still_skips_writing_an_unchanged_port()
	{
		var fixture = CreateFixture(publicListenerAvailable: false);

		var response = await fixture.Update.Handle(new UpdateNetworkSettingsRequest { PublicPort = ActivePort },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.PublicListenerUnavailable, Is.True);
			Assert.That(fixture.Repository.Writes, Is.Zero);
		});
	}

	[Test]
	public async Task Turning_tls_on_without_a_stored_certificate_is_rejected_without_writing_anything()
	{
		var fixture = CreateFixture();
		await fixture.Update.Handle(new UpdateNetworkSettingsRequest { PublicPort = ActivePort, TlsEnabled = false },
			CancellationToken.None);
		var writesBeforeTurningItOn = fixture.Repository.Writes;

		var response = await fixture.Update.Handle(
			new UpdateNetworkSettingsRequest { PublicPort = ActivePort, TlsEnabled = true },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error, Is.Not.Empty);
			Assert.That(fixture.Repository.Writes, Is.EqualTo(writesBeforeTurningItOn));
		});
	}

	[TestCase(0)]
	[TestCase(80)]
	[TestCase(1023)]
	[TestCase(65536)]
	public async Task An_out_of_range_https_port_is_rejected_without_writing_anything(int httpsPort)
	{
		var fixture = CreateFixture();
		var (cert, key, _) = ReissueCertificate();
		fixture.CertificateStore.Save(cert, key, PublicTlsCertificateSource.Custom);

		var response = await fixture.Update.Handle(new UpdateNetworkSettingsRequest
			{
				PublicPort = ActivePort,
				TlsEnabled = true,
				TlsMode = "Additional",
				TlsHttpsPort = httpsPort
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error, Is.Not.Empty);
			Assert.That(fixture.Repository.Writes, Is.Zero);
		});
	}

	[Test]
	public async Task An_https_port_equal_to_the_public_port_is_rejected_without_writing_anything()
	{
		var fixture = CreateFixture();
		var (cert, key, _) = ReissueCertificate();
		fixture.CertificateStore.Save(cert, key, PublicTlsCertificateSource.Custom);

		var response = await fixture.Update.Handle(new UpdateNetworkSettingsRequest
			{
				PublicPort = ActivePort,
				TlsEnabled = true,
				TlsMode = "Additional",
				TlsHttpsPort = ActivePort
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error, Is.Not.Empty);
			Assert.That(fixture.Repository.Writes, Is.Zero);
		});
	}

	[Test]
	public async Task An_https_port_equal_to_the_loopback_port_is_rejected_without_writing_anything()
	{
		var fixture = CreateFixture();
		var (cert, key, _) = ReissueCertificate();
		fixture.CertificateStore.Save(cert, key, PublicTlsCertificateSource.Custom);

		var response = await fixture.Update.Handle(new UpdateNetworkSettingsRequest
			{
				PublicPort = ActivePort,
				TlsEnabled = true,
				TlsMode = "Additional",
				TlsHttpsPort = LoopbackPort
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error, Is.Not.Empty);
			Assert.That(fixture.Repository.Writes, Is.Zero);
		});
	}

	// A stored HTTPS port that would be invalid in Additional mode must not block Replace mode: it is
	// not even looked at.
	[Test]
	public async Task Replace_mode_ignores_a_stored_https_port_and_saves_successfully()
	{
		var fixture = CreateFixture();
		var (cert, key, _) = ReissueCertificate();
		fixture.CertificateStore.Save(cert, key, PublicTlsCertificateSource.Custom);

		var response = await fixture.Update.Handle(new UpdateNetworkSettingsRequest
			{
				PublicPort = ActivePort,
				TlsEnabled = true,
				TlsMode = "Replace",
				TlsHttpsPort = 1 // out of range - must be ignored entirely in Replace mode
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(response.Error, Is.Null);
			Assert.That(response.TlsEnabled, Is.True);
			Assert.That(response.TlsMode, Is.EqualTo("Replace"));
		});
	}

	[Test]
	public async Task A_valid_tls_change_raises_a_restart_notification_and_reverting_it_dismisses_the_notification()
	{
		const int activeHttpsPort = 8443;
		const int newHttpsPort = 8444;
		var (cert, key, fingerprint) = ReissueCertificate();

		var fixture = CreateFixture(activeTlsMode: PublicTlsMode.Additional,
			activeTlsHttpsPort: activeHttpsPort,
			activeCertificateFingerprint: fingerprint);
		fixture.CertificateStore.Save(cert, key, PublicTlsCertificateSource.Custom);
		fixture.Repository.Seed(AppPreferenceService.TlsEnabledKey, true.ToString());
		fixture.Repository.Seed(AppPreferenceService.TlsModeKey, nameof(PublicTlsMode.Additional));
		fixture.Repository.Seed(AppPreferenceService.TlsHttpsPortKey,
			activeHttpsPort.ToString(CultureInfo.InvariantCulture));

		var baseline = await fixture.Get.Handle(new GetNetworkSettingsRequest(), CancellationToken.None);
		Assert.That(baseline.RestartRequired, Is.False);

		var changed = await fixture.Update.Handle(
			new UpdateNetworkSettingsRequest { PublicPort = ActivePort, TlsHttpsPort = newHttpsPort },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(changed.Success, Is.True);
			Assert.That(changed.RestartRequired, Is.True);
			Assert.That(fixture.Notifications.Snapshot(), Has.Count.EqualTo(1));
		});

		var reverted = await fixture.Update.Handle(
			new UpdateNetworkSettingsRequest { PublicPort = ActivePort, TlsHttpsPort = activeHttpsPort },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(reverted.Success, Is.True);
			Assert.That(reverted.RestartRequired, Is.False);
			Assert.That(fixture.Notifications.Snapshot(), Is.Empty);
		});
	}

	[Test]
	public async Task Changing_only_the_certificate_still_requires_a_restart()
	{
		const int httpsPort = 8443;
		var (cert1, key1, fingerprint1) = ReissueCertificate();
		var (cert2, key2, _) = ReissueCertificate();

		var fixture = CreateFixture(activeTlsMode: PublicTlsMode.Additional,
			activeTlsHttpsPort: httpsPort,
			activeCertificateFingerprint: fingerprint1);
		fixture.CertificateStore.Save(cert1, key1, PublicTlsCertificateSource.Custom);
		fixture.Repository.Seed(AppPreferenceService.TlsEnabledKey, true.ToString());
		fixture.Repository.Seed(AppPreferenceService.TlsModeKey, nameof(PublicTlsMode.Additional));
		fixture.Repository.Seed(AppPreferenceService.TlsHttpsPortKey, httpsPort.ToString(CultureInfo.InvariantCulture));

		var baseline = await fixture.Get.Handle(new GetNetworkSettingsRequest(), CancellationToken.None);
		Assert.That(baseline.RestartRequired, Is.False);

		var response = await fixture.UpdateCertificate.Handle(
			new UpdateNetworkTlsCertificateRequest { CertificatePem = cert2, PrivateKeyPem = key2 },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(response.RestartRequired, Is.True);
		});
	}

	[Test]
	public async Task A_mismatched_certificate_and_key_persists_nothing_and_leaves_the_previous_certificate_intact()
	{
		var (cert1, key1, fingerprint1) = ReissueCertificate();
		var (_, key2, _) = ReissueCertificate();

		var fixture = CreateFixture();
		fixture.CertificateStore.Save(cert1, key1, PublicTlsCertificateSource.Custom);

		var response = await fixture.UpdateCertificate.Handle(
			new UpdateNetworkTlsCertificateRequest { CertificatePem = cert1, PrivateKeyPem = key2 },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error, Is.Not.Empty);
			Assert.That(fixture.CertificateStore.ReadInfo()?.Fingerprint, Is.EqualTo(fingerprint1));
			Assert.That(fixture.CertificateStore.LoadServerCertificate().Certificate, Is.Not.Null);
		});
	}

	[Test]
	public async Task Reissuing_stores_a_certificate_issued_by_the_local_authority()
	{
		var fixture = CreateFixture();

		var response = await fixture.ReissueCertificate.Handle(new ReissueTlsCertificateRequest(),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(response.TlsCertificateConfigured, Is.True);
			Assert.That(response.TlsCertificateSource, Is.EqualTo(nameof(PublicTlsCertificateSource.LocalCa)));
			Assert.That(response.TlsAuthorityConfigured, Is.True);
			Assert.That(response.TlsCertificateFingerprint, Is.Not.Empty);
			Assert.That(fixture.CertificateStore.HasCertificate, Is.True);
		});
	}

	[Test]
	public async Task The_serialized_response_never_contains_the_private_key()
	{
		const string marker = "MacroDeckTestMarker-4f8ac2b17e91";
		var fixture = CreateFixture();
		var (cert, key, _) = ReissueCertificate();
		var markedKey = $"# {marker}\n{key}";

		var response = await fixture.UpdateCertificate.Handle(
			new UpdateNetworkTlsCertificateRequest { CertificatePem = cert, PrivateKeyPem = markedKey },
			CancellationToken.None);

		var json = JsonSerializer.Serialize(response);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(json, Does.Not.Contain(marker));
			Assert.That(json, Does.Not.Contain("PRIVATE KEY"));
		});
	}
}
