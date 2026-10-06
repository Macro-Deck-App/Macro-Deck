using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeckHost.Integrations.Obs;
using MacroDeckHost.Integrations.Obs.Actions;

namespace MacroDeckHost.Tests.UnitTests.Obs;

[TestFixture]
internal sealed class ObsRecordingTests
{
	private static ActionExecutionContext Context(Dictionary<string, object>? parameters = null)
		=> new() { Parameters = parameters ?? new Dictionary<string, object>() };

	private static (ObsConnection Connection, FakeObsClient Client, RecordingPublisher Publisher, int[] Refreshes)
		Create(bool connected = true)
	{
		var client = new FakeObsClient { IsConnected = connected };
		var publisher = new RecordingPublisher();
		var refreshes = new int[1];
		var connection = new ObsConnection(client,
			"ws://localhost:4455",
			null,
			events: new ObsEventEmitter(publisher),
			onVariablesChanged: () => refreshes[0]++);
		return (connection, client, publisher, refreshes);
	}

	private static IActionDefinition Action(ObsConnection connection, string id)
		=> ObsActions.Create(() => connection, new VariableApiAccessor()).Single(a => a.Id == id);

	[Test]
	public async Task Split_calls_the_client_and_succeeds()
	{
		var (connection, client, _, _) = Create();
		using var _ = connection;

		var result = await Action(connection, "split-record-file").CreateExecutor().ExecuteAsync(Context());

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(client.Calls, Is.EqualTo(new[] { "SplitRecordFile" }));
		});
	}

	[Test]
	public async Task Split_while_not_recording_reports_unavailable_not_a_connection_problem()
	{
		var (connection, client, _, _) = Create();
		using var _ = connection;
		client.RequestFailure = new ObsRequestException(501, "Output is not running");

		var result = await Action(connection, "split-record-file").CreateExecutor().ExecuteAsync(Context());

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
		});
	}

	[Test]
	public async Task Every_recording_action_fails_with_not_connected_and_sends_nothing_when_obs_is_offline()
	{
		var (connection, client, _, _) = Create(connected: false);
		using var _ = connection;
		var directory = new Dictionary<string, object> { [SetRecordDirectoryActionDefinition.DirectoryParameter] = "/rec" };

		var results = new[]
		{
			await Action(connection, "split-record-file").CreateExecutor().ExecuteAsync(Context()),
			await Action(connection, "create-record-chapter").CreateExecutor().ExecuteAsync(Context()),
			await Action(connection, "set-record-directory").CreateExecutor().ExecuteAsync(Context(directory))
		};

		Assert.Multiple(() =>
		{
			Assert.That(results.Select(r => r.ErrorCode), Is.All.EqualTo(ActionErrorCodes.NotConnected));
			Assert.That(client.Calls, Is.Empty);
		});
	}

	[TestCase(null, "<none>")]
	[TestCase("   ", "<none>")]
	[TestCase("  Boss fight ", "Boss fight")]
	public async Task Chapter_passes_a_trimmed_name_or_none(string? name, string expected)
	{
		var (connection, client, _, _) = Create();
		using var _ = connection;
		var parameters = new Dictionary<string, object>();
		if (name is not null)
		{
			parameters[CreateRecordChapterActionDefinition.ChapterNameParameter] = name;
		}

		var result = await Action(connection, "create-record-chapter").CreateExecutor().ExecuteAsync(Context(parameters));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(client.Calls, Is.EqualTo(new[] { $"CreateRecordChapter:{expected}" }));
		});
	}

	[Test]
	public async Task Chapter_while_not_recording_reports_unavailable()
	{
		var (connection, client, _, _) = Create();
		using var _ = connection;
		client.RequestFailure = new ObsRequestException(501, "Output is not running");

		var result = await Action(connection, "create-record-chapter").CreateExecutor().ExecuteAsync(Context());

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
	}

	[Test]
	public async Task An_obs_rejection_surfaces_its_message_as_provider_rejected()
	{
		var (connection, client, _, _) = Create();
		using var _ = connection;
		client.RequestFailure = new ObsRequestException(702, "Chapters need Hybrid MP4");

		var result = await Action(connection, "create-record-chapter").CreateExecutor().ExecuteAsync(Context());

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderRejected));
			Assert.That(result.ErrorMessage.Localized?.Arguments["message"]?.ToString(), Is.EqualTo("Chapters need Hybrid MP4"));
		});
	}

	[Test]
	public async Task Set_directory_sends_the_trimmed_path()
	{
		var (connection, client, _, _) = Create();
		using var _ = connection;

		var result = await Action(connection, "set-record-directory").CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object>
				{ [SetRecordDirectoryActionDefinition.DirectoryParameter] = "  D:\\Rec\\Game  " }));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(client.Calls, Is.EqualTo(new[] { "SetRecordDirectory:D:\\Rec\\Game" }));
		});
	}

	[Test]
	public async Task Set_directory_without_a_path_is_invalid_and_sends_nothing()
	{
		var (connection, client, _, _) = Create();
		using var _ = connection;

		var result = await Action(connection, "set-record-directory").CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object>
				{ [SetRecordDirectoryActionDefinition.DirectoryParameter] = "  " }));

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(client.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task Set_directory_surfaces_the_obs_error()
	{
		var (connection, client, _, _) = Create();
		using var _ = connection;
		client.RequestFailure = new ObsRequestException(400, "Invalid directory");

		var result = await Action(connection, "set-record-directory").CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object>
				{ [SetRecordDirectoryActionDefinition.DirectoryParameter] = "?" }));

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderRejected));
			Assert.That(result.ErrorMessage.Localized?.Arguments["message"]?.ToString(), Is.EqualTo("Invalid directory"));
		});
	}

	[Test]
	public void Record_file_changed_publishes_its_path_and_updates_the_variable()
	{
		var (connection, client, publisher, refreshes) = Create();
		using var _ = connection;

		client.RaiseRecordFileChanged("/rec/a.mp4");

		Assert.Multiple(() =>
		{
			Assert.That(publisher.Published, Has.Count.EqualTo(1));
			Assert.That(publisher.Published[0].EventId, Is.EqualTo("record-file-changed"));
			Assert.That(publisher.Published[0].Parameters?["path"], Is.EqualTo("/rec/a.mp4"));
			Assert.That(ObsVariables.Read(connection, "last_recording_file"), Is.EqualTo("/rec/a.mp4"));
			Assert.That(refreshes[0], Is.EqualTo(1));
		});
	}

	[Test]
	public void Screenshot_saved_publishes_its_path_and_updates_only_its_own_variable()
	{
		var (connection, client, publisher, _) = Create();
		using var _ = connection;

		client.RaiseScreenshotSaved("/shots/1.png");

		Assert.Multiple(() =>
		{
			Assert.That(publisher.Published.Select(p => p.EventId), Is.EqualTo(new[] { "screenshot-saved" }));
			Assert.That(publisher.Published[0].Parameters?["path"], Is.EqualTo("/shots/1.png"));
			Assert.That(ObsVariables.Read(connection, "last_screenshot"), Is.EqualTo("/shots/1.png"));
			Assert.That(ObsVariables.Read(connection, "last_recording_file"), Is.Null);
		});
	}

	[Test]
	public void The_last_recording_file_follows_each_split_and_survives_a_disconnect_and_reconnect()
	{
		var (connection, client, _, _) = Create();
		using var _ = connection;

		client.RaiseRecordFileChanged("/rec/part1.mp4");
		client.RaiseRecordFileChanged("/rec/part2.mp4");
		client.IsConnected = false;
		client.RaiseDisconnected("lost");
		var whileOffline = ObsVariables.Read(connection, "last_recording_file");
		client.IsConnected = true;
		client.RaiseConnected();

		Assert.Multiple(() =>
		{
			Assert.That(whileOffline, Is.EqualTo("/rec/part2.mp4"));
			Assert.That(ObsVariables.Read(connection, "last_recording_file"), Is.EqualTo("/rec/part2.mp4"));
		});
	}

	[Test]
	public async Task Events_after_dispose_publish_nothing()
	{
		var (connection, client, publisher, _) = Create();
		await connection.DisposeAsync();

		client.RaiseRecordFileChanged("/rec/late.mp4");
		client.RaiseScreenshotSaved("/shots/late.png");

		Assert.Multiple(() =>
		{
			Assert.That(publisher.Published, Is.Empty);
			Assert.That(connection.LastRecordingFilePath, Is.Null);
		});
	}

	[Test]
	public void Two_connections_keep_their_own_last_paths()
	{
		var (first, firstClient, _, _) = Create();
		var (second, secondClient, _, _) = Create();
		using var _ = first;
		using var __ = second;

		firstClient.RaiseRecordFileChanged("/a.mp4");
		secondClient.RaiseRecordFileChanged("/b.mp4");

		Assert.Multiple(() =>
		{
			Assert.That(first.LastRecordingFilePath, Is.EqualTo("/a.mp4"));
			Assert.That(second.LastRecordingFilePath, Is.EqualTo("/b.mp4"));
		});
	}

	[Test]
	public void The_new_actions_events_and_variables_are_registered_in_their_catalogs()
	{
		var (connection, _, _, _) = Create();
		using var _ = connection;

		var actions = ObsActions.Create(() => connection, new VariableApiAccessor()).Select(a => a.Id).ToList();
		var events = ObsEventDefinitions.All.Select(e => e.Id).ToList();
		var variables = ObsVariables.Declare("main", Guid.NewGuid()).Select(v => v.Name).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(actions,
				Is.SupersetOf(new[] { "split-record-file", "create-record-chapter", "set-record-directory" }));
			Assert.That(events, Is.SupersetOf(new[] { "record-file-changed", "screenshot-saved" }));
			Assert.That(variables, Is.SupersetOf(new[] { "obs_main_last_recording_file", "obs_main_last_screenshot" }));
		});
	}

	internal sealed class RecordingPublisher : IEventPublisher
	{
		public List<(string EventId, IReadOnlyDictionary<string, object?>? Parameters)> Published { get; } = [];

		public void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null)
			=> Published.Add((eventId, parameters));
	}
}
