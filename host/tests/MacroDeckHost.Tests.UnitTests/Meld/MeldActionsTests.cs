using System.Diagnostics;
using System.Globalization;
using MacroDeckHost.Integrations.Meld;
using MacroDeckHost.Integrations.Meld.Actions;
using MacroDeckHost.Tests.UnitTests.System;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Tests.UnitTests.Meld;

[TestFixture]
internal sealed class MeldActionsTests
{
	private const string SessionJson =
		"""
		{"items":{
		"scene1":{"current":true,"index":0,"name":"Scene One","staged":false,"type":"scene"},
		"layer1":{"parent":"scene1","index":0,"name":"Layer One","visible":true,"type":"layer"},
		"effect1":{"parent":"layer1","name":"Effect One","enabled":false,"type":"effect"},
		"track1":{"name":"Track One","muted":false,"monitoring":false,"type":"track"}
		}}
		""";

	private static readonly Uri _uri = new("ws://127.0.0.1:13376/");

	private CultureInfo _originalCulture = null!;

	[SetUp]
	public void SetUp()
	{
		_originalCulture = CultureInfo.CurrentCulture;
	}

	[TearDown]
	public void TearDown()
	{
		CultureInfo.CurrentCulture = _originalCulture;
	}

	[Test]
	public async Task Every_action_fails_with_NotConnected_when_there_is_no_connection()
	{
		var actions = MeldActions.Create(() => null, new VariableApiAccessor());

		Assert.Multiple(async () =>
		{
			foreach (var action in actions)
			{
				var result = await action.CreateExecutor().ExecuteAsync(Context());
				Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed), action.Id);
				Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotConnected), action.Id);
			}
		});

		await Task.CompletedTask;
	}

	[Test]
	public async Task Every_targeted_action_fails_with_NotConnected_not_NotFound_when_disconnected()
	{
		using var connection = new MeldConnection(() => new FakeQWebChannelClient(), _uri);
		var actions = MeldActions.Create(() => connection, new VariableApiAccessor());

		Assert.Multiple(async () =>
		{
			foreach (var action in actions)
			{
				var result = await action.CreateExecutor().ExecuteAsync(Context());
				Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed), action.Id);
				Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotConnected), action.Id);
			}
		});

		await Task.CompletedTask;
	}

	[Test]
	public void Twenty_actions_are_declared()
	{
		var actions = MeldActions.Create(() => null, new VariableApiAccessor());

		Assert.That(actions, Has.Count.EqualTo(20));
	}

	[Test]
	public async Task Blank_required_target_fails_with_InvalidParameter()
	{
		using var connection = await ConnectedAsync();
		var action = new SetLayerVisibilityActionDefinition(() => connection);

		var result = await action.CreateExecutor()
			.ExecuteAsync(Context([(MeldActionParameters.Layer, string.Empty)]));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
		});
	}

	[Test]
	public async Task A_target_resolves_by_id()
	{
		using var connection = await ConnectedAsync();
		var action = new SetEffectStateActionDefinition(() => connection);

		var result = await action.CreateExecutor()
			.ExecuteAsync(Context([(MeldActionParameters.Effect, "effect1"), (MeldActionParameters.Mode, "enable")]));

		Assert.That(result.Status, Is.Not.EqualTo(ActionResultStatus.Failed));
	}

	[Test]
	public async Task A_target_resolves_by_name()
	{
		using var connection = await ConnectedAsync();
		var action = new SetEffectStateActionDefinition(() => connection);

		var result = await action.CreateExecutor()
			.ExecuteAsync(Context([
				(MeldActionParameters.Effect, "Effect One"), (MeldActionParameters.Mode, "enable")
			]));

		Assert.That(result.Status, Is.Not.EqualTo(ActionResultStatus.Failed));
	}

	[Test]
	public async Task An_unresolved_target_fails_with_NotFound_naming_the_configured_value()
	{
		using var connection = await ConnectedAsync();
		var action = new SetEffectStateActionDefinition(() => connection);

		var result = await action.CreateExecutor()
			.ExecuteAsync(Context([(MeldActionParameters.Effect, "Nonexistent Effect")]));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("Nonexistent Effect"));
		});
	}

	[Test]
	public async Task A_number_parameter_parses_under_a_comma_decimal_culture()
	{
		CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

		var (connection, client) = await ConnectedWithClientAsync();
		using var connectionDisposable = connection;
		var action = new SetTrackVolumeActionDefinition(() => connection);

		await action.CreateExecutor()
			.ExecuteAsync(Context([(MeldActionParameters.Track, "track1"), (MeldActionParameters.Volume, "12.5")]));

		var args = client.ArgsOf(MeldObjects.SetGain);
		Assert.That(args, Is.Not.Null);
		Assert.That((double)args![1]!, Is.EqualTo(0.125).Within(0.0001));
	}

	[Test]
	public async Task Volume_clamps_at_zero()
	{
		var (connection, client) = await ConnectedWithClientAsync();
		using var connectionDisposable = connection;
		var action = new SetTrackVolumeActionDefinition(() => connection);

		await action.CreateExecutor()
			.ExecuteAsync(Context([(MeldActionParameters.Track, "track1"), (MeldActionParameters.Volume, -50d)]));

		var args = client.ArgsOf(MeldObjects.SetGain);
		Assert.That((double)args![1]!, Is.EqualTo(0d));
	}

	[Test]
	public async Task Volume_clamps_at_one_hundred()
	{
		var (connection, client) = await ConnectedWithClientAsync();
		using var connectionDisposable = connection;
		var action = new SetTrackVolumeActionDefinition(() => connection);

		await action.CreateExecutor()
			.ExecuteAsync(Context([(MeldActionParameters.Track, "track1"), (MeldActionParameters.Volume, 200d)]));

		var args = client.ArgsOf(MeldObjects.SetGain);
		Assert.That((double)args![1]!, Is.EqualTo(1d));
	}

	[Test]
	public async Task AdjustTrackVolume_without_a_cached_gain_fails_with_Unavailable()
	{
		using var connection = await ConnectedAsync();
		var action = new AdjustTrackVolumeActionDefinition(() => connection);

		var result = await action.CreateExecutor()
			.ExecuteAsync(Context([(MeldActionParameters.Track, "track1"), (MeldActionParameters.Amount, 5d)]));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
		});
	}

	[Test]
	public async Task GetTrackVolume_without_a_cached_gain_fails_with_Unavailable()
	{
		using var connection = await ConnectedAsync();
		var variables = new RecordingVariableApi();
		var action = new GetTrackVolumeActionDefinition(() => connection,
			new VariableApiAccessor { Current = variables });

		var result = await action.CreateExecutor()
			.ExecuteAsync(Context([(MeldActionParameters.Track, "track1"), (MeldActionParameters.Variable, "vol")]));

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
	}

	[Test]
	public async Task Dynamic_options_answer_from_the_cache_while_disconnected()
	{
		var action = new SetLayerVisibilityActionDefinition(() => null);

		var result = await action.GetDynamicOptionsAsync(new DynamicOptionsContext
			{
				ParameterName = MeldActionParameters.Layer,
				CurrentParameters = new Dictionary<string, object?>(StringComparer.Ordinal)
			},
			CancellationToken.None);

		Assert.That(result.Options, Is.Empty);
	}

	[Test]
	public async Task GetLayerVisibility_writes_a_boolean_creating_the_variable()
	{
		using var connection = await ConnectedAsync();
		var variables = new RecordingVariableApi();
		var action = new GetLayerVisibilityActionDefinition(() => connection,
			new VariableApiAccessor { Current = variables });

		await action.CreateExecutor()
			.ExecuteAsync(Context([
				(MeldActionParameters.Layer, "layer1"), (MeldActionParameters.Variable, "layer_visible")
			]));

		var handle = await variables.GetByNameAsync("layer_visible");
		Assert.Multiple(() =>
		{
			Assert.That(handle, Is.Not.Null);
			Assert.That(handle!.Value, Is.EqualTo(true));
			Assert.That(handle.Type, Is.EqualTo(VariableType.Boolean));
		});
	}

	[Test]
	public async Task GetLayerVisibility_fails_with_Unavailable_when_the_variable_API_is_unavailable()
	{
		using var connection = await ConnectedAsync();
		var action = new GetLayerVisibilityActionDefinition(() => connection, new VariableApiAccessor());

		var result = await action.CreateExecutor()
			.ExecuteAsync(Context([
				(MeldActionParameters.Layer, "layer1"), (MeldActionParameters.Variable, "layer_visible")
			]));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
		});
	}

	[Test]
	public async Task GetTrackMute_fails_with_Unavailable_when_the_variable_API_throws()
	{
		using var connection = await ConnectedAsync();
		var variables = new ThrowingVariableApi();
		var action = new GetTrackMuteActionDefinition(() => connection,
			new VariableApiAccessor { Current = variables });

		var result = await action.CreateExecutor()
			.ExecuteAsync(Context([
				(MeldActionParameters.Track, "track1"), (MeldActionParameters.Variable, "track_muted")
			]));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
		});
	}

	[Test]
	public async Task GetEffectState_writes_a_boolean_creating_the_variable()
	{
		using var connection = await ConnectedAsync();
		var variables = new RecordingVariableApi();
		var action = new GetEffectStateActionDefinition(() => connection,
			new VariableApiAccessor { Current = variables });

		await action.CreateExecutor()
			.ExecuteAsync(Context([
				(MeldActionParameters.Effect, "effect1"), (MeldActionParameters.Variable, "effect_enabled")
			]));

		var handle = await variables.GetByNameAsync("effect_enabled");
		Assert.Multiple(() =>
		{
			Assert.That(handle, Is.Not.Null);
			Assert.That(handle!.Value, Is.EqualTo(false));
			Assert.That(handle.Type, Is.EqualTo(VariableType.Boolean));
		});
	}

	[Test]
	public async Task GetTrackMute_writes_a_boolean_creating_the_variable()
	{
		using var connection = await ConnectedAsync();
		var variables = new RecordingVariableApi();
		var action = new GetTrackMuteActionDefinition(() => connection,
			new VariableApiAccessor { Current = variables });

		await action.CreateExecutor()
			.ExecuteAsync(Context([
				(MeldActionParameters.Track, "track1"), (MeldActionParameters.Variable, "track_muted")
			]));

		var handle = await variables.GetByNameAsync("track_muted");
		Assert.Multiple(() =>
		{
			Assert.That(handle, Is.Not.Null);
			Assert.That(handle!.Value, Is.EqualTo(false));
			Assert.That(handle.Type, Is.EqualTo(VariableType.Boolean));
		});
	}

	[Test]
	public async Task GetTrackVolume_writes_a_numeric_percentage_creating_the_variable()
	{
		var (connection, client) = await ConnectedWithClientAsync();
		using var connectionDisposable = connection;
		client.RaiseSignal(MeldObjects.Object,
			MeldObjects.GainUpdatedSignal,
			FakeQWebChannelClient.Parse("\"track1\""),
			FakeQWebChannelClient.Parse("0.5"),
			FakeQWebChannelClient.Parse("false"));
		await WaitForAsync(() => connection.TryGetGain("track1", out _), "the gain to be cached");

		var variables = new RecordingVariableApi();
		var action = new GetTrackVolumeActionDefinition(() => connection,
			new VariableApiAccessor { Current = variables });

		await action.CreateExecutor()
			.ExecuteAsync(Context([
				(MeldActionParameters.Track, "track1"), (MeldActionParameters.Variable, "track_volume")
			]));

		var handle = await variables.GetByNameAsync("track_volume");
		Assert.Multiple(() =>
		{
			Assert.That(handle, Is.Not.Null);
			Assert.That(handle!.Value, Is.EqualTo(50d));
			Assert.That(handle.Type, Is.EqualTo(VariableType.Numeric));
		});
	}

	private static ActionExecutionContext Context(IEnumerable<(string Key, object Value)>? parameters = null)
		=> new()
		{
			Parameters = (parameters ?? [])
				.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
		};

	[Test]
	public async Task A_track_mute_button_reports_the_tracks_real_mute_state()
	{
		using var connection = await ConnectedAsync();
		var provider = Provider(connection, "set-track-mute");

		var snapshot = await provider.GetActionStateAsync(
			new Dictionary<string, object?> { [MeldActionParameters.Track] = "track1" },
			CancellationToken.None);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("unmuted"));
	}

	[Test]
	public async Task A_track_button_resolves_its_target_by_name_as_well_as_by_id()
	{
		using var connection = await ConnectedAsync();
		var provider = Provider(connection, "set-track-monitoring");

		var snapshot = await provider.GetActionStateAsync(
			new Dictionary<string, object?> { [MeldActionParameters.Track] = "Track One" },
			CancellationToken.None);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("not-monitoring"));
	}

	[Test]
	public async Task A_layer_button_reports_whether_the_layer_is_visible()
	{
		using var connection = await ConnectedAsync();
		var provider = Provider(connection, "set-layer-visibility");

		var snapshot = await provider.GetActionStateAsync(
			new Dictionary<string, object?> { [MeldActionParameters.Layer] = "layer1" },
			CancellationToken.None);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("visible"));
	}

	[Test]
	public async Task An_effect_button_reports_whether_the_effect_is_enabled()
	{
		using var connection = await ConnectedAsync();
		var provider = Provider(connection, "set-effect-state");

		var snapshot = await provider.GetActionStateAsync(
			new Dictionary<string, object?> { [MeldActionParameters.Effect] = "effect1" },
			CancellationToken.None);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("disabled"));
	}

	[Test]
	public async Task A_scene_button_reports_whether_its_own_scene_is_the_one_showing()
	{
		using var connection = await ConnectedAsync();
		var provider = Provider(connection, "show-scene");

		var showing = await provider.GetActionStateAsync(
			new Dictionary<string, object?> { [MeldActionParameters.Scene] = "scene1" },
			CancellationToken.None);
		var missing = await provider.GetActionStateAsync(
			new Dictionary<string, object?> { [MeldActionParameters.Scene] = "scene-that-is-gone" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(showing!.ActiveStateId, Is.EqualTo("active"));
			Assert.That(missing!.ActiveStateId, Is.EqualTo("unavailable"));
		});
	}

	[Test]
	public async Task A_target_that_is_not_chosen_yet_reports_no_state_at_all()
	{
		using var connection = await ConnectedAsync();
		var provider = Provider(connection, "set-track-mute");

		var snapshot = await provider.GetActionStateAsync(new Dictionary<string, object?>(), CancellationToken.None);

		Assert.That(snapshot, Is.Null);
	}

	[Test]
	public async Task A_disconnected_meld_reports_unavailable_rather_than_off()
	{
		var provider = Provider(null, "set-track-mute");

		var snapshot = await provider.GetActionStateAsync(
			new Dictionary<string, object?> { [MeldActionParameters.Track] = "track1" },
			CancellationToken.None);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("unavailable"));
	}

	[Test]
	public async Task The_streaming_toggle_reports_whether_meld_is_streaming()
	{
		using var connection = await ConnectedAsync();
		var provider = Provider(connection, "toggle-streaming");

		var snapshot = await provider.GetActionStateAsync(new Dictionary<string, object?>(), CancellationToken.None);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("not-streaming"));
	}

	private static IStateProviderActionDefinition Provider(MeldConnection? connection, string actionId)
		=> (IStateProviderActionDefinition)MeldActions
			.Create(() => connection, new VariableApiAccessor())
			.Single(action => action.Id == actionId);

	private static async Task<MeldConnection> ConnectedAsync()
	{
		var (connection, _) = await ConnectedWithClientAsync();
		return connection;
	}

	private static async Task<(MeldConnection Connection, FakeQWebChannelClient Client)> ConnectedWithClientAsync()
	{
		var client = new FakeQWebChannelClient();
		client.Objects["meld"]
			= FakeQWebChannelClient.BuildMeldObjectInfo(sessionJson: SessionJson, supportsSetMuted: true);
		var connection = new MeldConnection(() => client, _uri);
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");
		return (connection, client);
	}

	private static async Task WaitForAsync(Func<bool> condition, string because)
	{
		var stopwatch = Stopwatch.StartNew();
		while (!condition() && stopwatch.Elapsed < TimeSpan.FromSeconds(5))
		{
			await Task.Delay(10);
		}

		Assert.That(condition(), Is.True, $"Timed out waiting for {because}.");
	}

	private sealed class ThrowingVariableApi : IVariableApi
	{
		public Task<IReadOnlyList<VariableHandle>> GetAllAsync() => throw new InvalidOperationException("boom");

		public Task<VariableHandle?> GetByNameAsync(string name) => throw new InvalidOperationException("boom");

		public Task<VariableHandle> CreateAsync(
			string name,
			VariableType type,
			object? initialValue = null,
			int? decimalPlaces = null,
			string? definitionId = null)
			=> throw new InvalidOperationException("boom");

		public Task SetValueAsync(Guid variableId, object? value) => throw new InvalidOperationException("boom");

		public Task DeleteAsync(Guid variableId) => throw new InvalidOperationException("boom");
	}
}
