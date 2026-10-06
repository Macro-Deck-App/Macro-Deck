using System.Diagnostics;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.Obs;
using MacroDeckHost.Integrations.Obs.Actions;

namespace MacroDeckHost.Tests.UnitTests.Obs;

[TestFixture]
internal sealed class ObsSceneCollectionTests
{
	private static readonly string[] _expectedSceneCollections = ["Gaming", "Podcast"];

	private static ActionExecutionContext Context(Dictionary<string, object>? parameters = null)
		=> new() { Parameters = parameters ?? new Dictionary<string, object>() };

	private static Dictionary<string, object> Collection(string name)
		=> new() { [SceneCollectionActionDefinition.SceneCollectionParameter] = name };

	private static (ObsConnection Connection, FakeObsClient Client, ObsRecordingTests.RecordingPublisher Publisher)
		Create(bool connected = true, ObsStatus? status = null)
	{
		var client = new FakeObsClient { IsConnected = connected, Status = status ?? new ObsStatus() };
		var publisher = new ObsRecordingTests.RecordingPublisher();
		var connection = new ObsConnection(client,
			"ws://localhost:4455",
			null,
			events: new ObsEventEmitter(publisher));
		if (connected)
		{
			client.RaiseStateChanged();
		}

		return (connection, client, publisher);
	}

	private static IActionDefinition Action(ObsConnection connection)
		=> ObsActions.Create(() => connection, new VariableApiAccessor()).Single(a => a.Id == "set-scene-collection");

	private static ObsNotReadyException NotReady() => new(new InvalidOperationException("207 NotReady"));

	[Test]
	public async Task Switches_obs_to_the_chosen_scene_collection()
	{
		var (connection, client, _) = Create();
		using var _ = connection;

		var result = await Action(connection).CreateExecutor().ExecuteAsync(Context(Collection("Podcast")));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(client.Calls, Is.EqualTo(new[] { "SetCurrentSceneCollection:Podcast" }));
		});
	}

	[Test]
	public async Task Without_a_scene_collection_it_is_invalid_and_sends_nothing()
	{
		var (connection, client, _) = Create();
		using var _ = connection;

		var result = await Action(connection).CreateExecutor().ExecuteAsync(Context());

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(client.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task While_obs_is_offline_it_fails_with_not_connected_and_sends_nothing()
	{
		var (connection, client, _) = Create(connected: false);
		using var _ = connection;

		var result = await Action(connection).CreateExecutor().ExecuteAsync(Context(Collection("Podcast")));

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotConnected));
			Assert.That(client.Calls, Is.Empty);
		});
	}

	[TestCase(ObsRequestException.ResourceNotFound, "ErrorCode: 600", ActionErrorCodes.NotFound, "SceneCollectionNotFound")]
	[TestCase(ObsRequestException.NotReady, "ErrorCode: 207", ActionErrorCodes.Unavailable, "SceneCollectionLoading")]
	[TestCase(ObsRequestException.TimedOut, "Request timed out", ActionErrorCodes.Timeout, "SceneCollectionSlowToLoad")]
	public async Task A_refused_switch_says_why_in_the_users_language(int code,
		string obsMessage,
		string errorCode,
		string messageKey)
	{
		var (connection, client, _) = Create();
		using var _ = connection;
		client.RequestFailure = new ObsRequestException(code, obsMessage);

		var result = await Action(connection).CreateExecutor().ExecuteAsync(Context(Collection("Deleted")));

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(errorCode));
			Assert.That(result.ErrorMessage.Localized?.Key.ToString(),
				Does.EndWith($"Integrations.Obs.Errors.{messageKey}"));
		});
	}

	[Test]
	public async Task A_collection_obs_cannot_find_is_named_in_the_error()
	{
		var (connection, client, _) = Create();
		using var _ = connection;
		client.RequestFailure = new ObsRequestException(ObsRequestException.ResourceNotFound, "ErrorCode: 600");

		var result = await Action(connection).CreateExecutor().ExecuteAsync(Context(Collection("Deleted")));

		Assert.That(result.ErrorMessage.Localized?.Arguments["name"]?.ToString(), Is.EqualTo("Deleted"));
	}

	[Test]
	public async Task Any_other_refusal_surfaces_obs_message_as_provider_rejected()
	{
		var (connection, client, _) = Create();
		using var _ = connection;
		client.RequestFailure = new ObsRequestException(702, "Scene collection switching is disabled");

		var result = await Action(connection).CreateExecutor().ExecuteAsync(Context(Collection("Podcast")));

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderRejected));
			Assert.That(result.ErrorMessage.Localized?.Arguments["message"]?.ToString(),
				Is.EqualTo("Scene collection switching is disabled"));
		});
	}

	[Test]
	public async Task Offers_the_scene_collections_obs_currently_has()
	{
		var (connection, client, _) = Create();
		using var _ = connection;
		client.SceneCollectionNames = ["Gaming", "Podcast"];

		var result = await ((IDynamicOptionsActionDefinition)Action(connection)).GetDynamicOptionsAsync(
			new DynamicOptionsContext
			{
				ParameterName = SceneCollectionActionDefinition.SceneCollectionParameter,
				CurrentParameters = new Dictionary<string, object?>()
			},
			CancellationToken.None);

		Assert.That(result.Options.Select(o => o.Value), Is.EqualTo(_expectedSceneCollections));
	}

	[Test]
	public async Task The_button_and_variable_follow_a_switch_made_in_obs()
	{
		var (connection, client, _) = Create(status: new ObsStatus { CurrentSceneCollection = "Gaming" });
		using var _ = connection;
		var provider = (IStateProviderActionDefinition)Action(connection);
		var gaming = new Dictionary<string, object?> { [SceneCollectionActionDefinition.SceneCollectionParameter] = "Gaming" };

		var before = await provider.GetActionStateAsync(gaming, default);
		client.Status = new ObsStatus { CurrentSceneCollection = "Podcast" };
		client.RaiseStateChanged();
		var after = await provider.GetActionStateAsync(gaming, default);

		Assert.Multiple(() =>
		{
			Assert.That(before!.ActiveStateId, Is.EqualTo("active"));
			Assert.That(after!.ActiveStateId, Is.EqualTo("inactive"));
			Assert.That(ObsVariables.Read(connection.State, "current_scene_collection"), Is.EqualTo("Podcast"));
		});
	}

	[Test]
	public async Task With_no_scene_collection_chosen_the_button_has_no_state()
	{
		var (connection, _, _) = Create(status: new ObsStatus { CurrentSceneCollection = "Gaming" });
		using var _ = connection;

		var snapshot = await ((IStateProviderActionDefinition)Action(connection))
			.GetActionStateAsync(new Dictionary<string, object?>(), default);

		Assert.That(snapshot, Is.Null);
	}

	[Test]
	public void While_obs_loads_a_collection_nothing_reads_as_stopped()
	{
		var (connection, client, publisher) = Create(status: new ObsStatus
		{
			CurrentSceneCollection = "Gaming", IsStreaming = true, IsRecording = true, CurrentScene = "Live"
		});
		using var _ = connection;
		publisher.Published.Clear();

		client.QueryStatusHandler = () => throw NotReady();
		client.RaiseStateChanged();
		var whileLoading = connection.State;

		client.QueryStatusHandler = null;
		client.Status = new ObsStatus
		{
			CurrentSceneCollection = "Podcast", IsStreaming = true, IsRecording = true, CurrentScene = "Live"
		};
		client.RaiseStateChanged();

		Assert.Multiple(() =>
		{
			Assert.That(whileLoading.IsStreaming, Is.True);
			Assert.That(whileLoading.IsRecording, Is.True);
			Assert.That(whileLoading.CurrentSceneCollection, Is.EqualTo("Gaming"));
			Assert.That(publisher.Published, Is.Empty);
			Assert.That(ObsVariables.Read(connection.State, "current_scene_collection"), Is.EqualTo("Podcast"));
		});
	}

	[Test]
	public async Task Connecting_while_obs_is_still_starting_shows_its_state_once_it_is_ready()
	{
		var client = new FakeObsClient { ConnectRaisesConnected = true, QueryStatusHandler = () => throw NotReady() };
		using var connection = new ObsConnection(client,
			"ws://localhost:4455",
			null,
			reconnectDelay: TimeSpan.FromMilliseconds(1),
			pollInterval: TimeSpan.FromMilliseconds(20));
		connection.Start();
		await WaitForAsync(() => connection.Status == ObsConnectionStatus.Connected);
		var whileStarting = connection.State;

		client.Status = new ObsStatus { IsRecording = true, RecordingTimecode = "00:00:05", CurrentSceneCollection = "Gaming" };
		client.QueryStatusHandler = null;
		await WaitForAsync(() => connection.State.IsConnected);

		Assert.Multiple(() =>
		{
			Assert.That(whileStarting.IsConnected, Is.False);
			Assert.That(connection.State.IsConnected, Is.True);
			Assert.That(connection.State.RecordingTimecode, Is.EqualTo("00:00:05"));
			Assert.That(connection.State.CurrentSceneCollection, Is.EqualTo("Gaming"));
		});
	}

	private static async Task WaitForAsync(Func<bool> condition)
	{
		var stopwatch = Stopwatch.StartNew();
		while (!condition() && stopwatch.Elapsed < TimeSpan.FromSeconds(5))
		{
			await Task.Delay(20);
		}
	}
}
