using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Migration;
using MacroDeck.Plugin.Protocol.Capabilities.MusicPlayer;
using MacroDeck.Plugin.Protocol.Capabilities.VirtualProfiles;
using MacroDeck.Plugin.Protocol.Capabilities.Weather;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Migration;
using MacroDeckHost.Application.Plugins.Capabilities;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
public class RemotePluginSnapshotRefresherTests
{
	private const string PluginId = "com.example.plugin";

	[Test]
	public async Task RefreshKindAsync_for_music_player_sends_the_instances_operation_at_the_provider_local_id()
	{
		var invoker = new RecordingInvoker
		{
			Result = new MusicPlayerInstancesResult
			{
				Instances =
				[
					new MusicPlayerInstanceDto { Id = "a1", DisplayName = "Account 1", HasCatalog = true },
					new MusicPlayerInstanceDto { Id = "a2", DisplayName = "Account 2", HasDevices = true }
				]
			}
		};
		var store = new InMemorySnapshotStore();
		await store.SaveAsync(RemotePluginCapabilitySnapshot.Empty(PluginId) with
		{
			MusicPlayerProviderName = "Spotify"
		});
		var refresher = new RemotePluginSnapshotRefresher(invoker, store);

		var result = await refresher.RefreshKindAsync(PluginId, CapabilityKinds.MusicPlayer, CancellationToken.None);

		string[] expectedInstanceIds = ["a1", "a2"];
		string[] expectedCatalogIds = ["a1"];
		string[] expectedDeviceIds = ["a2"];

		Assert.Multiple(() =>
		{
			// The operation actually sent over the wire - not describe, and not the "*" placeholder
			// describe uses, because instances is not local-id-agnostic like describe is.
			Assert.That(invoker.LastRequest!.Kind, Is.EqualTo(CapabilityKinds.MusicPlayer));
			Assert.That(invoker.LastRequest.Operation, Is.EqualTo(CapabilityOperations.MusicPlayer.Instances));
			Assert.That(invoker.LastRequest.LocalId, Is.EqualTo(ProviderCapabilityId.LocalId));

			Assert.That(result.AllSucceeded, Is.True);
			Assert.That(result.Snapshot.MusicPlayerInstances.Select(i => i.Id), Is.EqualTo(expectedInstanceIds));
			Assert.That(result.Snapshot.MusicPlayerCatalogInstanceIds, Is.EqualTo(expectedCatalogIds));
			Assert.That(result.Snapshot.MusicPlayerDeviceInstanceIds, Is.EqualTo(expectedDeviceIds));

			// instances carries no provider name (unlike describe) - a targeted refresh must not clobber it.
			Assert.That(result.Snapshot.MusicPlayerProviderName, Is.EqualTo("Spotify"));
		});
	}

	[Test]
	public async Task RefreshKindAsync_for_weather_sends_the_instances_operation_and_preserves_the_provider_name()
	{
		var invoker = new RecordingInvoker
		{
			Result = new WeatherInstancesResult
			{
				Instances = [new WeatherStationInstanceDto { Id = "berlin", DisplayName = "Berlin" }]
			}
		};
		var store = new InMemorySnapshotStore();
		await store.SaveAsync(
			RemotePluginCapabilitySnapshot.Empty(PluginId) with { WeatherProviderName = "Open-Meteo" });
		var refresher = new RemotePluginSnapshotRefresher(invoker, store);

		var result = await refresher.RefreshKindAsync(PluginId, CapabilityKinds.Weather, CancellationToken.None);

		string[] expectedInstanceIds = ["berlin"];

		Assert.Multiple(() =>
		{
			Assert.That(invoker.LastRequest!.Operation, Is.EqualTo(CapabilityOperations.Weather.Instances));
			Assert.That(invoker.LastRequest.LocalId, Is.EqualTo(ProviderCapabilityId.LocalId));
			Assert.That(result.Snapshot.WeatherInstances.Select(i => i.Id), Is.EqualTo(expectedInstanceIds));
			Assert.That(result.Snapshot.WeatherProviderName, Is.EqualTo("Open-Meteo"));
		});
	}

	[Test]
	public async Task
		RefreshKindAsync_for_virtual_profiles_sends_the_profiles_operation_and_preserves_the_provider_name()
	{
		var invoker = new RecordingInvoker
		{
			Result = new VirtualProfilesResult
			{
				Profiles =
				[
					new VirtualProfileDescriptorDto
					{
						Id = "p1",
						Name = "Profile 1",
						Layout = new ProfileLayoutDto { Kind = "Grid", Rows = 3, Columns = 4 },
						Folders = []
					}
				]
			}
		};
		var store = new InMemorySnapshotStore();
		await store.SaveAsync(RemotePluginCapabilitySnapshot.Empty(PluginId) with
		{
			ProfileProviderName = "Home Assistant"
		});
		var refresher = new RemotePluginSnapshotRefresher(invoker, store);

		var result = await refresher.RefreshKindAsync(PluginId,
			CapabilityKinds.VirtualProfiles,
			CancellationToken.None);

		string[] expectedProfileIds = ["p1"];

		Assert.Multiple(() =>
		{
			Assert.That(invoker.LastRequest!.Operation, Is.EqualTo(CapabilityOperations.VirtualProfiles.Profiles));
			Assert.That(invoker.LastRequest.LocalId, Is.EqualTo(ProviderCapabilityId.LocalId));
			Assert.That(result.Snapshot.Profiles.Select(p => p.Id), Is.EqualTo(expectedProfileIds));
			Assert.That(result.Snapshot.ProfileProviderName, Is.EqualTo("Home Assistant"));
		});
	}

	[Test]
	public async Task RefreshAsync_still_uses_describe_at_the_wildcard_local_id_for_music_player()
	{
		var invoker = new RecordingInvoker
		{
			Result = new MusicPlayerDescribePayload
			{
				ProviderName = "Spotify",
				Instances = [new MusicPlayerInstanceDto { Id = "a1", DisplayName = "Account 1" }]
			}
		};
		var refresher = new RemotePluginSnapshotRefresher(invoker, new InMemorySnapshotStore());

		await refresher.RefreshAsync(PluginId, [CapabilityKinds.MusicPlayer], CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(invoker.LastRequest!.Operation, Is.EqualTo(CapabilityOperations.MusicPlayer.Describe));
			Assert.That(invoker.LastRequest.LocalId, Is.EqualTo("*"));
		});
	}

	[Test]
	public async Task RefreshKindAsync_for_a_kind_with_no_narrower_operation_falls_back_to_describe()
	{
		var invoker = new RecordingInvoker
		{
			Result = new MacroDeck.Plugin.Protocol.Capabilities.Variables.VariableCatalogPayload
			{
				DeclaredVariables = [], Variables = []
			}
		};
		var refresher = new RemotePluginSnapshotRefresher(invoker, new InMemorySnapshotStore());

		await refresher.RefreshKindAsync(PluginId, CapabilityKinds.Variables, CancellationToken.None);

		Assert.That(invoker.LastRequest!.Operation, Is.EqualTo(CapabilityOperations.Variables.Describe));
	}

	/// <summary>
	/// A migration for an application this host cannot read has nothing to translate, so an unrecognised
	/// source name costs that one entry rather than the whole capability - a plugin built against a later
	/// SDK stays usable here.
	/// </summary>
	[Test]
	public async Task Describing_migrations_keeps_the_known_sources_and_drops_the_ones_this_host_cannot_read()
	{
		var invoker = new RecordingInvoker
		{
			Result = new MigrationDescribePayload
			{
				Migrations =
				[
					new MigrationDescriptorDto
					{
						Source = "MacroDeck2",
						ClaimedActionSources = ["OBS-WebSocket Plugin"],
						ClaimedSettingsSources = ["macro deck_obs-websocket plugin"]
					},
					new MigrationDescriptorDto
					{
						Source = "SomethingThisHostHasNeverHeardOf",
						ClaimedActionSources = ["whatever"],
						ClaimedSettingsSources = []
					}
				]
			}
		};
		var refresher = new RemotePluginSnapshotRefresher(invoker, new InMemorySnapshotStore());

		var result = await refresher.RefreshKindAsync(PluginId, CapabilityKinds.Migration, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.AllSucceeded, Is.True);
			Assert.That(result.Snapshot.Migrations.Select(migration => migration.Source),
				Is.EqualTo(new[] { MigrationSource.MacroDeck2 }));
			Assert.That(result.Snapshot.Migrations.Single().ClaimedActionSources,
				Is.EqualTo(new[] { "OBS-WebSocket Plugin" }));
		});
	}

	private sealed class RecordingInvoker : IPluginCapabilityInvoker
	{
		public object? Result { get; set; }

		public CapabilityInvokeRequest? LastRequest { get; private set; }

		public Task<JsonElement?> InvokeAsync(string pluginId,
			CapabilityInvokeRequest request,
			CancellationToken cancellationToken)
		{
			LastRequest = request;
			return Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(Result, PluginProtocolJson.Options));
		}

		public bool TryComplete(string pluginId, ProtocolEnvelope result) => false;

		public void AbortAll(string pluginId, ProtocolError reason)
		{
		}

		public bool IsLiveActionExecute(string pluginId, string correlationId) => false;
	}

	private sealed class InMemorySnapshotStore : IRemotePluginSnapshotStore
	{
		private readonly Dictionary<string, RemotePluginCapabilitySnapshot> _byPluginId = new(StringComparer.Ordinal);

		public RemotePluginCapabilitySnapshot GetSnapshot(string pluginId)
			=> _byPluginId.TryGetValue(pluginId, out var snapshot)
				? snapshot
				: RemotePluginCapabilitySnapshot.Empty(pluginId);

		public bool Has(string pluginId) => _byPluginId.ContainsKey(pluginId);

		public Task SaveAsync(RemotePluginCapabilitySnapshot snapshot, CancellationToken cancellationToken = default)
		{
			_byPluginId[snapshot.PluginId] = snapshot;
			return Task.CompletedTask;
		}
	}
}
