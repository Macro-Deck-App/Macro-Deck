using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Identity;
using MacroDeckHost.Application.Variables;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.MusicPlayer.Actions;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.Jellyfin;
using MacroDeckHost.Integrations.Jellyfin.Protocol;
using static MacroDeckHost.Tests.UnitTests.Jellyfin.JellyfinSessions;

namespace MacroDeckHost.Tests.UnitTests.Jellyfin;

[TestFixture]
internal sealed class JellyfinIntegrationTests
{
	private JellyfinTestContext _context = null!;
	private FakeJellyfinClient _client = null!;
	private JellyfinIntegration _integration = null!;

	[SetUp]
	public void SetUp()
	{
		_context = new JellyfinTestContext();
		_client = new FakeJellyfinClient();
		_integration = new JellyfinIntegration(_ => _client, timings: FastTimings);
	}

	[TearDown]
	public void TearDown() => _integration.Dispose();

	[Test]
	public async Task A_remembered_device_stays_selectable_while_the_server_is_unreachable()
	{
		var known = new JellyfinDeviceRegistry();
		known.Observe(JellyfinSessionMapper.Map([Session("tv", "Living Room TV")], "x"), DateTimeOffset.UtcNow, _ => false);
		_context.AddServer("Home", variableKey: "home", devices: known.Serialize());
		_client.Unreachable = true;

		await _integration.InitializeAsync(_context);

		var instances = _integration.GetInstances();
		Assert.That(instances.Select(instance => instance.DisplayName),
			Is.EqualTo(new[] { "Home", "Home · Living Room TV" }));

		var state = await _integration.GetPlayer(instances[1].Id)!.GetStateAsync();
		Assert.That(state.IsUnavailable, Is.True);
	}

	[Test]
	public async Task A_device_without_a_session_reads_as_disconnected_and_the_active_player_follows_playback()
	{
		_context.AddServer("Home", variableKey: "home");
		_client.Sessions = [Session("tv", "Living Room TV"), Session("phone", "Phone", item: Episode())];

		await InitializeAndWaitForDevicesAsync(2);

		var instances = _integration.GetInstances();
		var tv = instances.Single(instance => instance.DisplayName.EndsWith("Living Room TV", StringComparison.Ordinal));
		var tvState = await _integration.GetPlayer(tv.Id)!.GetStateAsync();
		var active = await _integration.GetPlayer(instances[0].Id)!.GetStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(tvState.PlaybackState, Is.EqualTo(PlaybackState.Stopped));
			Assert.That(active.TrackName, Is.EqualTo("Pilot"));
			Assert.That(active.Artists, Is.EqualTo(new[] { "Test Show" }));
			Assert.That(active.DeviceName, Is.EqualTo("Phone"));
			Assert.That(active.ArtworkId, Is.Not.Null);
		});
	}

	[Test]
	public async Task The_shared_music_player_actions_reach_the_chosen_device()
	{
		_context.AddServer("Home", variableKey: "home");
		_client.Sessions = [Session("tv", "Living Room TV", item: Movie())];
		await InitializeAndWaitForDevicesAsync(1);
		var tv = _integration.GetInstances()[1];

		var result = await RunAsync("toggle-play-pause", tv.Id);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(_client.Commands, Does.Contain("playstate:session-tv:PlayPause"));
		});
	}

	[Test]
	public async Task A_command_the_client_does_not_support_is_refused_without_reaching_Jellyfin()
	{
		_context.AddServer("Home", variableKey: "home");
		_client.Sessions = [Session("tv", "Living Room TV", item: Movie(), commands: ["VolumeUp"])];
		await InitializeAndWaitForDevicesAsync(1);
		var tv = _integration.GetInstances()[1];

		var own = await RunAsync("toggle-mute", tv.Id);
		var shared = await RunAsync("set-volume", tv.Id, ("volume", 30d));

		Assert.Multiple(() =>
		{
			Assert.That(own.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(own.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
			Assert.That(shared.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(_client.Commands, Is.Empty);
		});
	}

	[Test]
	public async Task Seeking_forward_moves_from_the_current_position()
	{
		_context.AddServer("Home", variableKey: "home");
		_client.Sessions = [Session("tv", item: Movie(), positionTicks: TimeSpan.FromSeconds(60).Ticks, paused: true)];
		await InitializeAndWaitForDevicesAsync(1);

		var result = await RunAsync("seek-forward", _integration.GetInstances()[1].Id, ("seconds", 30d));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(_client.Commands, Does.Contain($"playstate:session-tv:Seek:{TimeSpan.FromSeconds(90).Ticks}"));
		});
	}

	[Test]
	public async Task Seeking_media_the_client_cannot_seek_is_refused()
	{
		_context.AddServer("Home", variableKey: "home");
		var live = Session("tv", item: Movie(), positionTicks: TimeSpan.FromSeconds(60).Ticks);
		_client.Sessions = [live with { PlayState = live.PlayState! with { CanSeek = false } }];
		await InitializeAndWaitForDevicesAsync(1);

		var result = await RunAsync("seek-forward", _integration.GetInstances()[1].Id, ("seconds", 30d));

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
			Assert.That(_client.Commands, Is.Empty);
		});
	}

	[Test]
	public async Task The_default_player_follows_playback_on_any_server()
	{
		var second = new FakeJellyfinClient { Sessions = [Session("phone", "Phone", item: Track())] };
		_integration.Dispose();
		_integration = new JellyfinIntegration(settings => settings.BaseUri.Host == "cabin.local" ? second : _client,
			timings: FastTimings);
		_context.AddServer("Home", variableKey: "home");
		_context.AddServer("Cabin", url: "http://cabin.local:8096", variableKey: "cabin");
		_client.Sessions = [Session("tv")];

		await InitializeAndWaitForDevicesAsync(2);
		var result = await RunAsync("stop", string.Empty);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(second.Commands, Does.Contain("playstate:session-phone:Stop"));
		});
	}

	[Test]
	public async Task A_client_that_cannot_be_controlled_still_gets_variables_but_no_player()
	{
		_context.AddServer("Home", variableKey: "home");
		_client.Sessions = [Session("tv", "Bedroom TV", item: Movie(), controllable: false)];

		await InitializeAndWaitForDevicesAsync(1);

		Assert.Multiple(() =>
		{
			Assert.That(_integration.GetInstances().Select(instance => instance.DisplayName), Is.EqualTo(new[] { "Home" }));
			Assert.That(_integration.Variables.Select(variable => variable.Name), Does.Contain("jellyfin_home_bedroom_tv_title"));
		});
	}

	[Test]
	public async Task Volume_position_pause_and_mute_variables_control_the_device()
	{
		_context.AddServer("Home", variableKey: "home");
		_client.Sessions = [Session("tv", "Living Room TV", item: Movie())];
		await InitializeAndWaitForDevicesAsync(1);

		var volume = await Write("volume", 30d);
		var progress = await Write("progress_percent", 50d);
		var paused = await Write("is_paused", true);
		var muted = await Write("is_muted", "true");
		var title = await Write("title", "x");

		Assert.Multiple(async () =>
		{
			Assert.That(new[] { volume, progress, paused, muted }.Select(result => result.Status),
				Is.All.EqualTo(VariableWriteStatus.Applied));
			Assert.That(title.Status, Is.EqualTo(VariableWriteStatus.NotWritable));
			Assert.That(_client.Commands, Is.EqualTo(new[]
			{
				"command:session-tv:SetVolume:Volume=30",
				$"playstate:session-tv:Seek:{TimeSpan.FromMinutes(5).Ticks}",
				"playstate:session-tv:Pause",
				"command:session-tv:Mute:"
			}));
			var reading = await _integration.ReadAsync(Variable("jellyfin_home_living_room_tv_position").ResolvedId!);
			Assert.That(reading.Max, Is.EqualTo(600));
		});
	}

	[Test]
	public void Among_the_Jellyfin_actions_only_toggle_mute_has_a_button_state()
	{
		string[] jellyfinOnly =
			["stop", "seek-forward", "seek-backward", "mute", "unmute", "toggle-mute", "display-message", "play-media"];
		var stateful = _integration.Actions
			.Where(action => jellyfinOnly.Contains(action.Id) && action is IStateProviderActionDefinition)
			.Select(action => action.Id);

		Assert.That(stateful, Is.EqualTo(new[] { "toggle-mute" }));
	}

	private Task<VariableWriteResult> Write(string slot, object value)
		=> _integration.SetValueAsync(Variable($"jellyfin_home_living_room_tv_{slot}").ResolvedId!, value).AsTask();

	[Test]
	public async Task Devices_with_the_same_name_can_be_told_apart()
	{
		_context.AddServer("Home", variableKey: "home");
		_client.Sessions = [Session("a", "Firefox"), Session("b", "Firefox")];

		await InitializeAndWaitForDevicesAsync(2);

		Assert.That(_integration.GetInstances().Skip(1).Select(instance => instance.DisplayName),
			Is.EquivalentTo(new[] { "Home · Firefox (1)", "Home · Firefox (2)" }));
	}

	[Test]
	public async Task Device_variables_fall_back_to_typed_empties_when_playback_ends()
	{
		var entry = _context.AddServer("Home", variableKey: "home");
		_client.Sessions = [Session("tv", "Living Room TV", item: Movie())];
		await InitializeAndWaitForDevicesAsync(1);

		var title = Variable("jellyfin_home_living_room_tv_title");
		Assert.That((await _integration.ReadAsync(title.ResolvedId!)).Value, Is.EqualTo("Big Buck Bunny"));

		_client.Sessions = [Session("tv", "Living Room TV")];
		await WaitUntilAsync(() => _integration.Runtimes[0].Connection.State.ActiveSessions.Count == 0);

		Assert.Multiple(async () =>
		{
			Assert.That((await _integration.ReadAsync(title.ResolvedId!)).Value, Is.EqualTo(string.Empty));
			Assert.That((await _integration.ReadAsync(Variable("jellyfin_home_living_room_tv_is_playing").ResolvedId!)).Value,
				Is.EqualTo(false));
			Assert.That(_context.ConfigStore.Read(entry, JellyfinConfigKeys.Devices), Does.Contain("living_room_tv"));
		});
	}

	[Test]
	public async Task Device_variable_names_never_collide_across_servers()
	{
		_context.AddServer("Home", variableKey: "home");
		_context.AddServer("Home TV", variableKey: "home_tv");
		_client.Sessions = [Session("a", "TV x"), Session("b", "X")];

		await InitializeAndWaitForDevicesAsync(4);

		var names = _integration.Variables.Select(variable => variable.Name).ToList();
		Assert.That(names, Is.Unique);
	}

	[Test]
	public async Task Every_variable_has_a_valid_name_and_id_even_for_long_server_and_device_names()
	{
		_context.AddServer("Grandparents living room media server upstairs");
		_client.Sessions = [Session("a", "Samsung QLED 75 inch television in the guest room", item: Movie())];

		await InitializeAndWaitForDevicesAsync(1);

		Assert.That(_integration.Variables, Has.Count.GreaterThan(3));
		foreach (var variable in _integration.Variables)
		{
			Assert.Multiple(() =>
			{
				Assert.That(VariableNameSanitizer.IsValid(variable.Name), Is.True, variable.Name);
				Assert.That(variable.Name!.Length, Is.LessThanOrEqualTo(64), variable.Name);
				Assert.That(MacroDeckId.IsValidLocalId(variable.ResolvedId, LocalIdKind.Declared), Is.True,
					variable.ResolvedId);
			});
		}
	}

	[Test]
	public async Task Playback_events_are_published_with_the_server_they_came_from()
	{
		var entry = _context.AddServer("Home", variableKey: "home");
		_client.Sessions = [Session("tv")];
		await InitializeAndWaitForDevicesAsync(1);

		_client.Sessions = [Session("tv", item: Movie())];
		await WaitUntilAsync(() => _context.Published.Events.Any(e => e.Id == JellyfinEventIds.PlaybackStarted));

		_client.Sessions = [Session("tv", item: Movie(), paused: true)];
		await WaitUntilAsync(() => _context.Published.Events.Any(e => e.Id == JellyfinEventIds.PlaybackPaused));
		_client.Sessions = [Session("tv")];
		await WaitUntilAsync(() => _context.Published.Events.Any(e => e.Id == JellyfinEventIds.PlaybackStopped));

		var started = _context.Published.Events.First(e => e.Id == JellyfinEventIds.PlaybackStarted);
		Assert.That(started.Payload[JellyfinEventPayload.Configuration], Is.EqualTo(entry.ToString("D")));
	}

	private VariableDefinitionSnapshot Variable(string name)
		=> _integration.Variables.Where(variable => variable.Name == name)
			.Select(variable => new VariableDefinitionSnapshot(variable.ResolvedId))
			.Single();

	private async Task<ActionResult> RunAsync(string actionId, string instanceId, params (string Name, object Value)[] extra)
	{
		var parameters = new Dictionary<string, object> { [MusicPlayerActions.InstanceParameterName] = instanceId };
		foreach (var (name, value) in extra)
		{
			parameters[name] = value;
		}

		var action = _integration.Actions.Single(candidate => candidate.Id == actionId);
		return await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext { Parameters = parameters });
	}

	private async Task InitializeAndWaitForDevicesAsync(int devices)
	{
		await _integration.InitializeAsync(_context);
		await WaitUntilAsync(() => _integration.Runtimes.Sum(runtime => runtime.Devices.Devices.Count) >= devices &&
			_integration.Runtimes.All(runtime => runtime.Connection.State.IsConnected));
	}

	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
			{
				Assert.Fail("Timed out waiting for the Jellyfin state.");
			}

			await Task.Delay(10);
		}
	}

	private sealed record VariableDefinitionSnapshot(string? ResolvedId);
}
