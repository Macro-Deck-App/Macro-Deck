using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.MusicPlayer.Actions;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests;

[TestFixture]
internal sealed class MusicPlayerActionsTests
{
	private static readonly string[] _expectedInstanceOptionValues = ["", "a", "b"];

	[Test]
	public async Task VolumeUp_RaisesVolumeByStepAndClampsToHundred()
	{
		var player = new FakeMusicPlayer { Volume = 95 };
		var actions = MusicPlayerActions.Common(_ => player, () => [new("p1", "Player")]);

		await ExecuteAsync(actions, "volume-up");

		Assert.That(player.SetVolumeArg, Is.EqualTo(100));
	}

	[Test]
	public async Task VolumeDown_LowersVolumeByStepAndClampsToZero()
	{
		var player = new FakeMusicPlayer { Volume = 5 };
		var actions = MusicPlayerActions.Common(_ => player, () => [new("p1", "Player")]);

		await ExecuteAsync(actions, "volume-down");

		Assert.That(player.SetVolumeArg, Is.EqualTo(0));
	}

	[Test]
	public async Task VolumeUp_AppliesStepWithinRange()
	{
		var player = new FakeMusicPlayer { Volume = 40 };
		var actions = MusicPlayerActions.Common(_ => player, () => [new("p1", "Player")]);

		await ExecuteAsync(actions, "volume-up");

		Assert.That(player.SetVolumeArg, Is.EqualTo(50));
	}

	[Test]
	public async Task ToggleShuffle_ReportsWhetherShuffleIsOn()
	{
		var player = new FakeMusicPlayer { Shuffle = true };
		var provider = Provider(player, "toggle-shuffle");

		var snapshot = await provider.GetActionStateAsync(_noParameters, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(snapshot!.ActiveStateId, Is.EqualTo("on"));
			Assert.That(snapshot.States.Select(state => state.Id), Is.EqualTo(_shuffleStateIds));
		});
	}

	[TestCase(RepeatMode.Off, "off")]
	[TestCase(RepeatMode.Track, "track")]
	[TestCase(RepeatMode.Context, "context")]
	public async Task SetRepeatMode_ReportsTheModeThePlayerIsIn(RepeatMode mode, string expectedStateId)
	{
		var player = new FakeMusicPlayer { Repeat = mode };
		var provider = Provider(player, "set-repeat-mode");

		var snapshot = await provider.GetActionStateAsync(_noParameters, CancellationToken.None);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo(expectedStateId));
	}

	[Test]
	public async Task ToggleShuffle_WithNoPlayerAtAll_IsUnavailableRatherThanOff()
	{
		var actions = MusicPlayerActions.Common(_ => null, () => []);
		var provider = (IStateProviderActionDefinition)actions.Single(action => action.Id == "toggle-shuffle");

		var snapshot = await provider.GetActionStateAsync(_noParameters, CancellationToken.None);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("unavailable"));
	}

	[Test]
	public async Task SetRepeatMode_WithAPlayerThatCannotBeReached_IsUnavailable()
	{
		var player = new FakeMusicPlayer { Connected = false };
		var provider = Provider(player, "set-repeat-mode");

		var snapshot = await provider.GetActionStateAsync(_noParameters, CancellationToken.None);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("unavailable"));
	}

	private static readonly Dictionary<string, object?> _noParameters = new(StringComparer.Ordinal);

	private static readonly string[] _shuffleStateIds = ["off", "on", "unavailable"];

	private static IStateProviderActionDefinition Provider(IMusicPlayer player, string actionId)
		=> (IStateProviderActionDefinition)MusicPlayerActions
			.Common(_ => player, () => [new MusicPlayerInstance("p1", "Player")])
			.Single(action => action.Id == actionId);

	[TestCase(PlaybackState.Playing, "paused")]
	[TestCase(PlaybackState.Paused, "playing")]
	[TestCase(PlaybackState.Stopped, "playing")]
	public async Task TogglePlayPause_ReportsTheExpectedPlaybackState(
		PlaybackState playbackState,
		string expectedStateId)
	{
		var player = new FakeMusicPlayer { PlaybackState = playbackState };
		var actions = MusicPlayerActions.Common(_ => player, () => [new("p1", "Player")]);

		var result = await ExecuteAsync(actions, "toggle-play-pause");

		Assert.That(result.ExpectedStateId, Is.EqualTo(expectedStateId));
	}

	[Test]
	public async Task PublicStateActionConstructor_PreservesLegacyExecutionWithoutAnExpectedState()
	{
		var player = new FakeMusicPlayer { PlaybackState = PlaybackState.Playing };
		var action = new MusicPlayerStateActionDefinition(_ => player,
			() => [new("p1", "Player")],
			"custom-state",
			"Custom State",
			"Custom provider state action",
			[],
			(_, _, _, _) => Task.CompletedTask,
			state => new ActionStateSnapshot(
				[new ActionStateDefinition("one", "One"), new ActionStateDefinition("two", "Two")],
				state is null ? "one" : "two"));

		var result = await ExecuteAsync([action], "custom-state");

		Assert.That(result.ExpectedStateId, Is.Null);
	}

	[Test]
	public async Task InstanceDynamicOptions_ListsInstancesAndFirstAvailable()
	{
		var player = new FakeMusicPlayer();
		var actions = MusicPlayerActions.Common(_ => player,
			() => [new("a", "Alpha"), new("b", "Beta")]);

		var action = actions.Single(a => a.Id == "play");
		var result = await ((IDynamicOptionsActionDefinition)action).GetDynamicOptionsAsync(new DynamicOptionsContext
			{
				ParameterName = MusicPlayerActions.InstanceParameterName,
				CurrentParameters = new Dictionary<string, object?>()
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Options.Select(o => o.Value), Is.EqualTo(_expectedInstanceOptionValues));
			Assert.That(TestLocalization.Resolve(result.Options[0].Label), Is.EqualTo("First available"));
		});
	}

	[Test]
	public async Task InstanceParameter_PlaceholderNamesTheEmptyOption()
	{
		var player = new FakeMusicPlayer();
		var actions = MusicPlayerActions.Common(_ => player, () => [new("a", "Alpha")]);

		var action = actions.Single(a => a.Id == "play");
		var instance = action.Parameters.Single(p => p.Name == MusicPlayerActions.InstanceParameterName);
		var options = await ((IDynamicOptionsActionDefinition)action).GetDynamicOptionsAsync(new DynamicOptionsContext
			{
				ParameterName = MusicPlayerActions.InstanceParameterName,
				CurrentParameters = new Dictionary<string, object?>()
			},
			CancellationToken.None);

		Assert.That(instance.Placeholder, Is.EqualTo(options.Options.Single(o => o.Value == "").Label));
	}

	[Test]
	public async Task PlayTrack_WithFixedItem_CallsPlayItem()
	{
		var player = new FakeCatalogMusicPlayer();
		var actions = new[] { MusicPlayerActions.PlayTrack("app.test", _ => player, () => [new("p1", "Player")]) };

		var result = await ExecuteAsync(actions, "play-track", "track", "spotify:track:abc");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(result.ErrorCode, Is.Null);
			Assert.That(player.PlayedItem?.Id, Is.EqualTo("spotify:track:abc"));
			Assert.That(player.PlayedItem?.Kind, Is.EqualTo(MusicPlayerCatalogItemKind.Track));
		});
	}

	[Test]
	public async Task PlayTrack_OnBarePlayer_FailsWithUnavailableAndDoesNotCallTheDecoy()
	{
		var player = new FakeMusicPlayer();
		var interactions = new RecordingInteractions();
		var actions = new[] { MusicPlayerActions.PlayTrack("app.test", _ => player, () => [new("p1", "Player")]) };

		var result = await ExecuteAsync(actions,
			"play-track",
			"track",
			"spotify:track:abc",
			interactions: interactions);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Is.Not.Empty);
			Assert.That(player.WasCalled, Is.False);
			Assert.That(interactions.Picks, Is.Empty);
		});
	}

	[Test]
	public async Task PlayTrack_NoItemConfigured_OnBarePlayer_FailsWithUnavailableWithoutOpeningThePicker()
	{
		var player = new FakeMusicPlayer();
		var interactions = new RecordingInteractions();
		var actions = new[] { MusicPlayerActions.PlayTrack("app.test", _ => player, () => [new("p1", "Player")]) };

		var result = await ExecuteAsync(actions, "play-track", "track", "", interactions: interactions);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
			Assert.That(interactions.Picks, Is.Empty);
			Assert.That(player.WasCalled, Is.False);
		});
	}

	[Test]
	public async Task PlayTrack_WithEmptyItem_AndInteractions_RequestsPicker()
	{
		var player = new FakeCatalogMusicPlayer();
		var interactions = new RecordingInteractions();
		var actions = new[] { MusicPlayerActions.PlayTrack("app.test", _ => player, () => [new("p1", "Player")]) };

		var result = await ExecuteAsync(actions, "play-track", "track", "", interactions: interactions);

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Accepted));
		Assert.That(TestLocalization.Resolve(result.Message), Is.EqualTo("Waiting for an item to be picked."));
		Assert.That(player.PlayedItem, Is.Null);
		Assert.That(interactions.Picks, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(interactions.Picks[0].InstanceId, Is.EqualTo("app.test::p1"));
			Assert.That(interactions.Picks[0].Kind, Is.EqualTo(MusicPlayerCatalogItemKind.Track));
		});
	}

	[Test]
	public async Task PlayTrack_WithEmptyItem_AndNoInteractions_StillReportsAccepted()
	{
		var player = new FakeCatalogMusicPlayer();
		var actions = new[] { MusicPlayerActions.PlayTrack("app.test", _ => player, () => [new("p1", "Player")]) };

		ActionResult? result = null;
		Assert.DoesNotThrowAsync(async () =>
			result = await ExecuteAsync(actions, "play-track", "track", "", interactions: null));

		Assert.That(player.PlayedItem, Is.Null);
		Assert.That(result!.Status, Is.EqualTo(ActionResultStatus.Accepted));
	}

	[Test]
	public async Task NoPlayerConfigured_FailsWithNotConfigured()
	{
		var actions = MusicPlayerActions.Common(_ => null, () => []);

		var result = await ExecuteAsync(actions, "play");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotConfigured));
		});
	}

	[Test]
	public void ACancelledCommand_PropagatesRatherThanReportingProviderError()
	{
		var player = new FakeMusicPlayer { ThrowOnPlay = new OperationCanceledException() };
		var actions = MusicPlayerActions.Common(_ => player, () => [new("p1", "Player")]);

		Assert.ThrowsAsync<OperationCanceledException>(async () => await ExecuteAsync(actions, "play"));
	}

	[Test]
	public void ACancelledItemPlay_PropagatesRatherThanReportingProviderError()
	{
		var player = new FakeCatalogMusicPlayer { ThrowOnPlayItem = new OperationCanceledException() };
		var actions = new[] { MusicPlayerActions.PlayTrack("app.test", _ => player, () => [new("p1", "Player")]) };

		Assert.ThrowsAsync<OperationCanceledException>(async () =>
			await ExecuteAsync(actions, "play-track", "track", "spotify:track:abc"));
	}

	[Test]
	public async Task PlayOnDevice_WithFixedDevice_TransfersWithStartPlaybackTrue()
	{
		var player = new FakeDeviceMusicPlayer();
		var actions = new[] { MusicPlayerActions.PlayOnDevice("app.test", _ => player, () => [new("p1", "Player")]) };

		await ExecuteAsync(actions, "play-on-device", MusicPlayerActions.DeviceParameterName, "device-1");

		Assert.That(player.Transferred, Is.EqualTo(("device-1", true)));
	}

	[Test]
	public async Task TransferPlayback_WithFixedDevice_TransfersWithStartPlaybackFalse()
	{
		var player = new FakeDeviceMusicPlayer();
		var actions = new[]
			{ MusicPlayerActions.TransferPlayback("app.test", _ => player, () => [new("p1", "Player")]) };

		await ExecuteAsync(actions, "transfer-playback", MusicPlayerActions.DeviceParameterName, "device-1");

		Assert.That(player.Transferred, Is.EqualTo(("device-1", false)));
	}

	[Test]
	public async Task PlayOnDevice_WithEmptyDevice_AndInteractions_RequestsPickerWithStartPlaybackTrue()
	{
		var player = new FakeDeviceMusicPlayer();
		var interactions = new RecordingInteractions();
		var actions = new[] { MusicPlayerActions.PlayOnDevice("app.test", _ => player, () => [new("p1", "Player")]) };

		await ExecuteAsync(actions,
			"play-on-device",
			MusicPlayerActions.DeviceParameterName,
			"",
			interactions: interactions);

		Assert.That(player.Transferred, Is.Null);
		Assert.That(interactions.DevicePicks, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(interactions.DevicePicks[0].InstanceId, Is.EqualTo("app.test::p1"));
			Assert.That(interactions.DevicePicks[0].StartPlayback, Is.True);
		});
	}

	[Test]
	public async Task TransferPlayback_WithEmptyDevice_AndInteractions_RequestsPickerWithStartPlaybackFalse()
	{
		var player = new FakeDeviceMusicPlayer();
		var interactions = new RecordingInteractions();
		var actions = new[]
			{ MusicPlayerActions.TransferPlayback("app.test", _ => player, () => [new("p1", "Player")]) };

		await ExecuteAsync(actions,
			"transfer-playback",
			MusicPlayerActions.DeviceParameterName,
			"",
			interactions: interactions);

		Assert.That(player.Transferred, Is.Null);
		Assert.That(interactions.DevicePicks, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(interactions.DevicePicks[0].InstanceId, Is.EqualTo("app.test::p1"));
			Assert.That(interactions.DevicePicks[0].StartPlayback, Is.False);
		});
	}

	[Test]
	public async Task PlayOnDevice_WithEmptyDevice_AndNoInteractions_IsAcceptedWithoutTransferring()
	{
		var player = new FakeDeviceMusicPlayer();
		var actions = new[] { MusicPlayerActions.PlayOnDevice("app.test", _ => player, () => [new("p1", "Player")]) };

		var result = await ExecuteAsync(actions,
			"play-on-device",
			MusicPlayerActions.DeviceParameterName,
			"",
			interactions: null);

		Assert.That(player.Transferred, Is.Null);
		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Accepted));
	}

	[Test]
	public async Task PlayOnDevice_OnPlayerWithoutDeviceCapability_FailsWithUnavailable()
	{
		var player = new FakeMusicPlayer();
		var actions = new[] { MusicPlayerActions.PlayOnDevice("app.test", _ => player, () => [new("p1", "Player")]) };

		var result = await ExecuteAsync(actions,
			"play-on-device",
			MusicPlayerActions.DeviceParameterName,
			"device-1");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
		});
	}

	[Test]
	public async Task ItemDynamicOptions_StillSuggestsFromABrowseOnlyPlayer()
	{
		var player = new FakeBrowseOnlyMusicPlayer
		{
			Catalog = [new MusicPlayerCatalogItem("t1", "Track One", MusicPlayerCatalogItemKind.Track)]
		};
		var actions = new[] { MusicPlayerActions.PlayTrack("app.test", _ => player, () => [new("p1", "Player")]) };
		var action = actions.Single(a => a.Id == "play-track");

		var result = await ((IDynamicOptionsActionDefinition)action).GetDynamicOptionsAsync(new DynamicOptionsContext
			{
				ParameterName = "track",
				CurrentParameters =
					new Dictionary<string, object?> { [MusicPlayerActions.InstanceParameterName] = "p1" }
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Options, Has.Count.EqualTo(1));
			Assert.That(result.Options[0].Value, Is.EqualTo("t1"));
			Assert.That(TestLocalization.Resolve(result.Options[0].Label), Is.EqualTo("Track One"));
		});
	}

	[Test]
	public async Task DeviceDynamicOptions_ListsDevicesAndHonoursFilter()
	{
		var player = new FakeDeviceMusicPlayer
		{
			Devices =
			[
				new MusicPlayerDevice("d1", "Living Room", "Speaker"),
				new MusicPlayerDevice("d2", "Kitchen", "Speaker"),
				new MusicPlayerDevice("d3", "Desktop", "Computer")
			]
		};
		var actions = new[] { MusicPlayerActions.PlayOnDevice("app.test", _ => player, () => [new("p1", "Player")]) };
		var action = actions.Single(a => a.Id == "play-on-device");

		var result = await ((IDynamicOptionsActionDefinition)action).GetDynamicOptionsAsync(new DynamicOptionsContext
			{
				ParameterName = MusicPlayerActions.DeviceParameterName,
				Filter = "kitchen",
				CurrentParameters =
					new Dictionary<string, object?> { [MusicPlayerActions.InstanceParameterName] = "p1" }
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Options, Has.Count.EqualTo(1));
			Assert.That(result.Options[0].Value, Is.EqualTo("d2"));
			Assert.That(TestLocalization.Resolve(result.Options[0].Label), Is.EqualTo("Kitchen (Speaker)"));
			Assert.That(result.AllowsCustomValue, Is.True);
		});
	}

	[Test]
	public async Task DeviceDynamicOptions_DegradesToEmptyWithCustomValue_WhenProviderThrows()
	{
		var player = new FakeDeviceMusicPlayer { ThrowOnGetDevices = new InvalidOperationException("boom") };
		var actions = new[] { MusicPlayerActions.PlayOnDevice("app.test", _ => player, () => [new("p1", "Player")]) };
		var action = actions.Single(a => a.Id == "play-on-device");

		var result = await ((IDynamicOptionsActionDefinition)action).GetDynamicOptionsAsync(new DynamicOptionsContext
			{
				ParameterName = MusicPlayerActions.DeviceParameterName,
				CurrentParameters =
					new Dictionary<string, object?> { [MusicPlayerActions.InstanceParameterName] = "p1" }
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Options, Is.Empty);
			Assert.That(result.AllowsCustomValue, Is.True);
		});
	}

	private static Task<ActionResult> ExecuteAsync(
		IReadOnlyList<IActionDefinition> actions,
		string actionId,
		string? itemParameterName = null,
		string? itemValue = null,
		IActionInteractions? interactions = null)
	{
		var action = actions.Single(a => a.Id == actionId);
		var parameters = new Dictionary<string, object>
		{
			[MusicPlayerActions.InstanceParameterName] = "p1"
		};
		if (itemParameterName is not null && itemValue is not null)
		{
			parameters[itemParameterName] = itemValue;
		}

		var context = new ActionExecutionContext { Parameters = parameters, Interactions = interactions };
		return action.CreateExecutor().ExecuteAsync(context);
	}

	private sealed class RecordingInteractions : IActionInteractions
	{
		public List<(string InstanceId, MusicPlayerCatalogItemKind Kind)> Picks { get; } = new();

		public List<(string InstanceId, bool StartPlayback)> DevicePicks { get; } = new();

		public void RequestItemPicker(string? originClientId,
			string instanceId,
			MusicPlayerCatalogItemKind kind,
			string? prompt = null)
			=> Picks.Add((instanceId, kind));

		public void RequestDevicePicker(string? originClientId,
			string instanceId,
			bool startPlayback,
			string? prompt = null)
			=> DevicePicks.Add((instanceId, startPlayback));
	}

	/// <summary>
	/// A bare <see cref="IMusicPlayer"/> that also declares a non-interface <c>PlayItemAsync</c> method -
	/// a decoy that a wrong implementation dispatching via reflection/dynamic rather than the
	/// <see cref="ICatalogMusicPlayer"/> gate would call anyway.
	/// </summary>
	private class FakeMusicPlayer : IMusicPlayer
	{
		public int Volume { get; set; }
		public PlaybackState PlaybackState { get; set; }
		public bool Shuffle { get; set; }
		public RepeatMode Repeat { get; set; }
		public bool Connected { get; set; } = true;

		public int? SetVolumeArg { get; private set; }

		public Exception? ThrowOnPlay { get; set; }

		public bool WasCalled { get; private set; }

		public Task PlayItemAsync(MusicPlayerCatalogItem item, CancellationToken cancellationToken = default)
		{
			WasCalled = true;
			return Task.CompletedTask;
		}

		public Task<MusicPlayerState> GetStateAsync(CancellationToken cancellationToken = default)
			=> Task.FromResult(new MusicPlayerState
			{
				IsConnected = Connected,
				VolumePercent = Volume,
				PlaybackState = PlaybackState,
				ShuffleEnabled = Shuffle,
				RepeatMode = Repeat
			});

		public Task<MusicPlayerArtwork?> GetArtworkAsync(string artworkId,
			CancellationToken cancellationToken = default)
			=> Task.FromResult<MusicPlayerArtwork?>(null);

		public Task PlayAsync(CancellationToken cancellationToken = default)
		{
			if (ThrowOnPlay is not null)
			{
				throw ThrowOnPlay;
			}

			return Task.CompletedTask;
		}

		public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task TogglePlayPauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task NextAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task PreviousAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
		{
			SetVolumeArg = volumePercent;
			return Task.CompletedTask;
		}

		public Task SetShuffleAsync(bool enabled, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task SetRepeatModeAsync(RepeatMode mode, CancellationToken cancellationToken = default) =>
			Task.CompletedTask;
	}

	private sealed class FakeCatalogMusicPlayer : ICatalogMusicPlayer
	{
		public int Volume { get; set; }

		public MusicPlayerCatalogItem? PlayedItem { get; private set; }

		public Exception? ThrowOnPlayItem { get; set; }

		public Task<MusicPlayerState> GetStateAsync(CancellationToken cancellationToken = default)
			=> Task.FromResult(new MusicPlayerState { IsConnected = true, VolumePercent = Volume });

		public Task<MusicPlayerArtwork?> GetArtworkAsync(string artworkId,
			CancellationToken cancellationToken = default)
			=> Task.FromResult<MusicPlayerArtwork?>(null);

		public Task PlayAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task PlayItemAsync(MusicPlayerCatalogItem item, CancellationToken cancellationToken = default)
		{
			if (ThrowOnPlayItem is not null)
			{
				throw ThrowOnPlayItem;
			}

			PlayedItem = item;
			return Task.CompletedTask;
		}

		public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task TogglePlayPauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task NextAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task PreviousAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default) =>
			Task.CompletedTask;

		public Task SetShuffleAsync(bool enabled, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task SetRepeatModeAsync(RepeatMode mode, CancellationToken cancellationToken = default) =>
			Task.CompletedTask;

		public Task<IReadOnlyList<MusicPlayerCatalogItem>> GetCatalogAsync(
			string instanceId,
			MusicPlayerCatalogItemKind kind,
			string? filter,
			CancellationToken cancellationToken)
			=> Task.FromResult<IReadOnlyList<MusicPlayerCatalogItem>>([]);
	}

	private sealed class FakeBrowseOnlyMusicPlayer : FakeMusicPlayer, IMusicPlayerCatalogProvider
	{
		public IReadOnlyList<MusicPlayerCatalogItem> Catalog { get; set; } = [];

		public Task<IReadOnlyList<MusicPlayerCatalogItem>> GetCatalogAsync(
			string instanceId,
			MusicPlayerCatalogItemKind kind,
			string? filter,
			CancellationToken cancellationToken)
			=> Task.FromResult(Catalog);
	}

	private sealed class FakeDeviceMusicPlayer : FakeMusicPlayer, IMusicPlayerDeviceProvider
	{
		public IReadOnlyList<MusicPlayerDevice> Devices { get; set; } = [];

		public Exception? ThrowOnGetDevices { get; set; }

		public (string DeviceId, bool StartPlayback)? Transferred { get; private set; }

		public Task<IReadOnlyList<MusicPlayerDevice>> GetDevicesAsync(CancellationToken cancellationToken)
		{
			if (ThrowOnGetDevices is not null)
			{
				throw ThrowOnGetDevices;
			}

			return Task.FromResult(Devices);
		}

		public Task TransferPlaybackAsync(string deviceId, bool startPlayback, CancellationToken cancellationToken)
		{
			Transferred = (deviceId, startPlayback);
			return Task.CompletedTask;
		}
	}
}
