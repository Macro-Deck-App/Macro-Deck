using System.Net;
using System.Net.NetworkInformation;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Network.Discovery;
using MacroDeckHost.Application.Network.Tls;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Network.Discovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Tests.UnitTests.Network.Discovery;

[TestFixture]
internal sealed class ServiceAdvertisementBackgroundServiceTests
{
	private sealed class RecordingAdvertiser : IServiceAdvertiser
	{
		public List<ServiceAdvertisement> Published { get; } = [];

		public int Withdrawals { get; private set; }

		public bool FailNextPublish { get; set; }

		public event Action? StateChanged
		{
			add { }
			remove { }
		}

		public bool IsAvailable => true;

		public bool NeedsRepublish { get; set; }

		public void CheckLiveness()
		{
		}

		public void Publish(ServiceAdvertisement advertisement)
		{
			if (FailNextPublish)
			{
				FailNextPublish = false;
				throw new InvalidOperationException("The responder is not running");
			}

			Published.Add(advertisement);
			NeedsRepublish = false;
		}

		public void Withdraw() => Withdrawals++;

		public void Dispose()
		{
		}
	}

	private sealed class FakeInterfaces : INetworkInterfaceSnapshotProvider
	{
		public List<NetworkInterfaceSnapshot> Interfaces { get; } =
		[
			new(17, "en0", "Wi-Fi", NetworkInterfaceType.Wireless80211, true, [IPAddress.Parse("192.168.20.13")])
		];

		public IReadOnlyList<NetworkInterfaceSnapshot> GetInterfaces() => Interfaces;
	}

	private sealed class FakeHostNames : IHostNameProvider
	{
		public string MachineName => "Studio PC";

		public IReadOnlyList<string> GetHostNames() => [];
	}

	private sealed class InMemoryPreferences : IAppPreferenceRepository
	{
		private readonly Dictionary<string, AppPreferenceEntity> _store = new();

		public Task<AppPreferenceEntity?> GetByKey(string key) => Task.FromResult(_store.GetValueOrDefault(key));

		public Task SetValue(string key, string value)
		{
			_store[key] = new AppPreferenceEntity { Key = key, Value = value };
			return Task.CompletedTask;
		}
	}

	private sealed class FakeBuildEnvironment : IBuildEnvironment
	{
		public string Version => "0.0.0-test";

		public bool IsBeta => false;

		public BuildChannel Channel => BuildChannel.Production;
	}

	private sealed class StartedHostLifetime : IHostApplicationLifetime
	{
		public CancellationToken ApplicationStarted => new(true);

		public CancellationToken ApplicationStopping => CancellationToken.None;

		public CancellationToken ApplicationStopped => CancellationToken.None;

		public void StopApplication()
		{
		}
	}

	private sealed record Harness(
		ServiceAdvertisementBackgroundService Service,
		RecordingAdvertiser Advertiser,
		FakeInterfaces Interfaces,
		InMemoryPreferences Preferences);

	private static Harness Create(PublicEndpointSet? endpoints = null)
	{
		var preferences = new InMemoryPreferences();
		var listenerState = new HostListenerState(endpoints ?? PublicEndpointSet.HttpOnly(8193), false);
		var services = new ServiceCollection();
		services.AddScoped<IAppPreferenceService>(_ =>
			new AppPreferenceService(preferences, new FakeBuildEnvironment(), listenerState));
		var provider = services.BuildServiceProvider();
		var advertiser = new RecordingAdvertiser();
		var interfaces = new FakeInterfaces();
		var service = new ServiceAdvertisementBackgroundService(new StartedHostLifetime(),
			provider.GetRequiredService<IServiceScopeFactory>(),
			advertiser,
			new FakeHostNames(),
			listenerState,
			interfaces);

		return new Harness(service, advertiser, interfaces, preferences);
	}

	[Test]
	public async Task An_unchanged_network_is_published_only_once()
	{
		var harness = Create();

		await harness.Service.Reconcile();
		await harness.Service.Reconcile();

		Assert.Multiple(() =>
		{
			Assert.That(harness.Advertiser.Published, Has.Count.EqualTo(1));
			Assert.That(harness.Advertiser.Published[0].WireName, Is.EqualTo("Studio PC"));
			Assert.That(harness.Advertiser.Published[0].Port, Is.EqualTo(8193));
		});
	}

	[Test]
	public async Task A_new_lan_interface_is_published()
	{
		var harness = Create();
		await harness.Service.Reconcile();

		harness.Interfaces.Interfaces.Add(new NetworkInterfaceSnapshot(5,
			"en7",
			"USB LAN",
			NetworkInterfaceType.Ethernet,
			true,
			[IPAddress.Parse("192.168.30.4")]));
		await harness.Service.Reconcile();

		Assert.That(harness.Advertiser.Published.Last().Interfaces, Has.Count.EqualTo(2));
	}

	[Test]
	public async Task A_registration_the_responder_lost_is_published_again()
	{
		var harness = Create();
		await harness.Service.Reconcile();

		harness.Advertiser.NeedsRepublish = true;
		await harness.Service.Reconcile();

		Assert.That(harness.Advertiser.Published, Has.Count.EqualTo(2));
	}

	[Test]
	public async Task Turning_discovery_off_withdraws_the_advertisement()
	{
		var harness = Create();
		await harness.Service.Reconcile();

		await harness.Preferences.SetValue(AppPreferenceService.DiscoveryEnabledKey, bool.FalseString);
		await harness.Service.Reconcile();

		Assert.Multiple(() =>
		{
			Assert.That(harness.Advertiser.Withdrawals, Is.EqualTo(1));
			Assert.That(harness.Advertiser.Published, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_host_without_a_plain_http_listener_is_never_advertised()
	{
		var harness = Create(PublicEndpointSet.HttpsReplacingHttp(8193));

		await harness.Service.Reconcile();

		Assert.That(harness.Advertiser.Published, Is.Empty);
	}

	[Test]
	public async Task A_failing_responder_does_not_escape_and_is_retried()
	{
		var harness = Create();
		harness.Advertiser.FailNextPublish = true;

		Assert.DoesNotThrowAsync(harness.Service.Reconcile);
		await harness.Service.Reconcile();

		Assert.That(harness.Advertiser.Published, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task Turning_discovery_off_after_a_partly_failed_publish_still_withdraws()
	{
		var harness = Create();
		harness.Advertiser.FailNextPublish = true;
		await harness.Service.Reconcile();

		await harness.Preferences.SetValue(AppPreferenceService.DiscoveryEnabledKey, bool.FalseString);
		await harness.Service.Reconcile();

		Assert.That(harness.Advertiser.Withdrawals, Is.EqualTo(1));
	}

	[Test]
	public async Task Stopping_the_host_withdraws_the_advertisement()
	{
		var harness = Create();
		await harness.Service.Reconcile();

		await harness.Service.StopAsync(CancellationToken.None);

		Assert.That(harness.Advertiser.Withdrawals, Is.EqualTo(1));
	}
}
