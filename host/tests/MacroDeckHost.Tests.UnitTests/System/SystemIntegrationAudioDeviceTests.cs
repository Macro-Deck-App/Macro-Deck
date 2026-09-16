using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Integrations.System;
using MacroDeckHost.Integrations.System.Focus;
using MacroDeckHost.Integrations.System.Notifications;
using MacroDeckHost.Integrations.System.Volume;
using MacroDeck.Sdk;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;

namespace MacroDeckHost.Tests.UnitTests.System;

public class SystemIntegrationAudioDeviceTests
{
	private static readonly AudioDevice _speakers = new("speakers-uid", "MacBook Pro Speakers", AudioFlow.Output, true);
	private static readonly AudioDevice _microphone = new("mic-uid", "MacBook Pro Microphone", AudioFlow.Input, true);
	private static readonly AudioDevice _headset = new("headset-uid", "Arctis 7", AudioFlow.Output, false);

	private readonly List<SystemIntegration> _started = [];

	[TearDown]
	public async Task TearDown()
	{
		foreach (var integration in _started)
		{
			await integration.ShutdownAsync();
		}

		FocusedApplicationSnapshot.Current.Set(null);
	}

	private async Task<SystemIntegration> StartAsync(
		FakeVolumeService volume,
		InMemoryKnownAudioDeviceStore? store = null,
		CountingInvalidationSignal? invalidation = null,
		IVariableRefreshSignal? refresh = null)
	{
		var integration = new SystemIntegration(new FakeApplicationService { IsSupported = true },
			volume,
			new SilentNotificationService(),
			new FakeSystemMetricsService(),
			new FakePowerService(true));
		if (store is not null)
		{
			integration.UseKnownAudioDeviceStore(store);
		}

		if (invalidation is not null)
		{
			integration.UseVariablePollingInvalidation(invalidation);
		}

		if (refresh is not null)
		{
			integration.UseVariableRefreshSignal(refresh);
		}

		await integration.InitializeAsync(new AudioTestContext(new RecordingVariableApi()));
		_started.Add(integration);
		return integration;
	}

	private static string IdOf(SystemIntegration integration, string name)
		=> integration.Variables.Single(variable => variable.Name == name).ResolvedId!;

	private static FakeVolumeService VolumeWith(params AudioDevice[] devices)
	{
		var volume = new FakeVolumeService();
		volume.Devices.AddRange(devices);
		return volume;
	}

	[Test]
	public async Task The_input_variables_read_and_write_the_default_input()
	{
		var volume = VolumeWith(_microphone);
		volume.Set(AudioTarget.DefaultInput, 0.4f, true);
		var integration = await StartAsync(volume);

		var level = await integration.ReadAsync("system-input-volume-percent");
		var muted = await integration.ReadAsync("system-input-muted");
		var write = await integration.SetValueAsync("system-input-volume-percent", 70);

		Assert.Multiple(() =>
		{
			Assert.That(level.Value, Is.EqualTo(40));
			Assert.That(muted.Value, Is.EqualTo(true));
			Assert.That(write.Status, Is.EqualTo(VariableWriteStatus.Applied));
			Assert.That(volume.VolumeOf(AudioTarget.DefaultInput)!.Value, Is.EqualTo(0.7f).Within(0.0001));
		});
	}

	[Test]
	public async Task Every_device_gets_a_volume_and_a_mute_variable_named_after_it()
	{
		var volume = VolumeWith(_speakers, _microphone);
		volume.Set(new AudioTarget(AudioFlow.Output, _speakers.Id), 0.25f, false);
		volume.Set(new AudioTarget(AudioFlow.Input, _microphone.Id), 0.8f, true);
		var integration = await StartAsync(volume);

		var speakers = await integration.ReadAsync(IdOf(integration, "system_audio_output_macbook_pro_speakers_volume_percent"));
		var speakersMuted = await integration.ReadAsync(IdOf(integration, "system_audio_output_macbook_pro_speakers_muted"));
		var microphone = await integration.ReadAsync(IdOf(integration, "system_audio_input_macbook_pro_microphone_volume_percent"));
		var microphoneMuted = await integration.ReadAsync(IdOf(integration, "system_audio_input_macbook_pro_microphone_muted"));

		Assert.Multiple(() =>
		{
			Assert.That(speakers.Value, Is.EqualTo(25));
			Assert.That(speakersMuted.Value, Is.EqualTo(false));
			Assert.That(microphone.Value, Is.EqualTo(80));
			Assert.That(microphoneMuted.Value, Is.EqualTo(true));
		});
	}

	[Test]
	public async Task Writing_a_device_volume_sets_that_device_only()
	{
		var volume = VolumeWith(_speakers, _headset);
		volume.Volume = 0.5f;
		var integration = await StartAsync(volume);

		var result = await integration.SetValueAsync(IdOf(integration, "system_audio_output_arctis_7_volume_percent"), 30);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(VariableWriteStatus.Applied));
			Assert.That(volume.VolumeOf(new AudioTarget(AudioFlow.Output, _headset.Id))!.Value, Is.EqualTo(0.3f).Within(0.0001));
			Assert.That(volume.Volume, Is.EqualTo(0.5f));
		});
	}

	[Test]
	public async Task The_existing_default_output_variables_are_unchanged()
	{
		var volume = VolumeWith(_speakers);
		volume.Volume = 0.6f;
		volume.Muted = true;
		var integration = await StartAsync(volume);

		Assert.Multiple(async () =>
		{
			Assert.That((await integration.ReadAsync("system-volume-percent")).Value, Is.EqualTo(60));
			Assert.That((await integration.ReadAsync("system-muted")).Value, Is.EqualTo(true));
			Assert.That(integration.Variables.Select(v => v.Name),
				Does.Contain("system_volume_percent").And.Contain("system_muted"));
		});
	}

	[Test]
	public async Task Initialization_declares_stored_devices_and_asks_the_host_to_register_them()
	{
		var store = new InMemoryKnownAudioDeviceStore();
		store.Devices.Add(new KnownAudioDevice("output", "old-uid", "0a1b2c3d", "old_speakers", "Old Speakers"));
		var invalidation = new CountingInvalidationSignal();

		var integration = await StartAsync(VolumeWith(), store, invalidation);

		Assert.Multiple(() =>
		{
			Assert.That(integration.Variables.Select(v => v.Name), Does.Contain("system_audio_output_old_speakers_volume_percent"));
			Assert.That(invalidation.Count, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task A_device_plugged_in_later_is_declared_and_registered()
	{
		var volume = VolumeWith(_speakers);
		var invalidation = new CountingInvalidationSignal();
		var integration = await StartAsync(volume, invalidation: invalidation);

		volume.Devices.Add(_headset);
		await integration.RefreshAudioDevicesAsync();

		Assert.Multiple(() =>
		{
			Assert.That(integration.Variables.Select(v => v.Name), Does.Contain("system_audio_output_arctis_7_volume_percent"));
			Assert.That(invalidation.Count, Is.EqualTo(2));
		});
	}

	[Test]
	public async Task An_unchanged_device_list_does_not_ask_the_host_to_register_again()
	{
		var invalidation = new CountingInvalidationSignal();
		var integration = await StartAsync(VolumeWith(_speakers), invalidation: invalidation);

		await integration.RefreshAudioDevicesAsync();

		Assert.That(invalidation.Count, Is.EqualTo(1));
	}

	[Test]
	public async Task A_removed_device_keeps_its_variables_and_reads_unavailable()
	{
		var volume = VolumeWith(_speakers, _headset);
		volume.Set(new AudioTarget(AudioFlow.Output, _headset.Id), 0.4f, false);
		var integration = await StartAsync(volume);
		var id = IdOf(integration, "system_audio_output_arctis_7_volume_percent");

		volume.Devices.Remove(_headset);
		await integration.RefreshAudioDevicesAsync();

		var reading = await integration.ReadAsync(id);
		var write = await integration.SetValueAsync(id, 50);

		Assert.Multiple(() =>
		{
			Assert.That(integration.Variables.Select(v => v.ResolvedId), Does.Contain(id));
			Assert.That(reading.Value, Is.Null);
			Assert.That(write.Status, Is.EqualTo(VariableWriteStatus.Unavailable));
		});
	}

	[Test]
	public async Task After_a_restart_an_absent_device_is_declared_under_its_original_name()
	{
		var store = new InMemoryKnownAudioDeviceStore();
		var first = await StartAsync(VolumeWith(_speakers, _headset), store);
		var id = IdOf(first, "system_audio_output_arctis_7_volume_percent");
		await first.ShutdownAsync();

		var restarted = await StartAsync(VolumeWith(_speakers), store);

		Assert.Multiple(async () =>
		{
			Assert.That(IdOf(restarted, "system_audio_output_arctis_7_volume_percent"), Is.EqualTo(id));
			Assert.That((await restarted.ReadAsync(id)).Value, Is.Null);
		});
	}

	[Test]
	public async Task A_renamed_device_keeps_its_variable_name()
	{
		var volume = VolumeWith(_headset);
		var integration = await StartAsync(volume);

		volume.Devices[0] = _headset with { Name = "2- Arctis 7 (USB)" };
		await integration.RefreshAudioDevicesAsync();

		Assert.That(integration.Variables.Select(v => v.Name), Does.Contain("system_audio_output_arctis_7_volume_percent"));
	}

	[Test]
	public async Task A_twin_device_gets_its_own_name_and_the_first_one_keeps_its_name()
	{
		var volume = VolumeWith(_headset);
		var integration = await StartAsync(volume);

		volume.Devices.Add(_headset with { Id = "second-headset-uid" });
		await integration.RefreshAudioDevicesAsync();

		var names = integration.Variables.Select(v => v.Name!).Where(n => n.EndsWith("_volume_percent", StringComparison.Ordinal)).ToList();
		Assert.Multiple(() =>
		{
			Assert.That(names, Does.Contain("system_audio_output_arctis_7_volume_percent"));
			Assert.That(names, Has.Some.Matches(@"^system_audio_output_arctis_7_[0-9a-f]{8}_volume_percent$"));
		});
	}

	[Test]
	public async Task An_unreadable_device_file_is_never_overwritten()
	{
		var store = new InMemoryKnownAudioDeviceStore { Readable = false };

		var integration = await StartAsync(VolumeWith(_speakers), store);

		Assert.Multiple(() =>
		{
			Assert.That(store.Saves, Is.Zero);
			Assert.That(integration.Variables.Select(v => v.Name), Does.Contain("system_audio_output_macbook_pro_speakers_muted"));
		});
	}

	[Test]
	public async Task A_new_device_is_saved_to_the_store()
	{
		var store = new InMemoryKnownAudioDeviceStore();

		await StartAsync(VolumeWith(_speakers), store);

		Assert.That(store.Devices.Select(device => device.DeviceId), Is.EqualTo(new[] { _speakers.Id }));
	}

	[Test]
	public async Task A_default_output_change_also_refreshes_that_devices_own_variables()
	{
		var volume = VolumeWith(_speakers);
		var refresh = new VariableRefreshSignal();
		var integration = await StartAsync(volume, refresh: refresh);

		volume.RaiseChanged();

		Assert.That(refresh.DrainDefinitionsFor(SystemIntegration.IntegrationId),
			Is.EquivalentTo(new[]
			{
				"system-volume-percent",
				"system-muted",
				IdOf(integration, "system_audio_output_macbook_pro_speakers_volume_percent"),
				IdOf(integration, "system_audio_output_macbook_pro_speakers_muted")
			}));
	}


	private sealed class InMemoryKnownAudioDeviceStore : IKnownAudioDeviceStore
	{
		public List<KnownAudioDevice> Devices { get; } = [];

		public bool Readable { get; init; } = true;

		public int Saves { get; private set; }

		public bool TryLoad(out IReadOnlyList<KnownAudioDevice> devices)
		{
			devices = Readable ? [.. Devices] : [];
			return Readable;
		}

		public bool Save(IEnumerable<KnownAudioDevice> devices)
		{
			Saves++;
			Devices.Clear();
			Devices.AddRange(devices);
			return true;
		}
	}

	private sealed class CountingInvalidationSignal : IVariablePollingInvalidationSignal
	{
		public int Count { get; private set; }

		public void MarkStale(string integrationId) => Count++;

		public IReadOnlyCollection<string> DrainStale() => [];
	}

	private sealed class SilentNotificationService : INotificationService
	{
		public bool IsSupported => true;

		public Task ShowAsync(string title, string message, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;
	}

	private sealed class AudioTestContext(IVariableApi variables) : IIntegrationContext
	{
		public IVariableApi Variables { get; } = variables;

		public IUserVariableApi UserVariables => null!;

		public IIntegrationConfig Config => throw new NotSupportedException();

		public IDeckNavigator Deck => throw new NotSupportedException();

		public IScriptApi Scripts => throw new NotSupportedException();

		public IWidgetApi Widgets => throw new NotSupportedException();

		public IEventPublisher Events => throw new NotSupportedException();

		public IUserNotifier Notifications => throw new NotSupportedException();
	}
}
