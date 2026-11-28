using System.Diagnostics;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.Obs.Actions;
using MacroDeckHost.Integrations.Obs.Connections;
using MacroDeckHost.Integrations.Obs.Protocol;

namespace MacroDeckHost.Tests.UnitTests.Obs;

[TestFixture]
internal sealed class ObsCommandRejectionTests
{
	private static ActionExecutionContext Context(Dictionary<string, object>? parameters = null)
	{
		return new ActionExecutionContext { Parameters = parameters ?? new Dictionary<string, object>() };
	}

	private static Dictionary<string, object> Scene(string name)
	{
		return new Dictionary<string, object> { [SceneActionDefinition.SceneParameter] = name };
	}

	private static Task<ActionResult> RunAsync(
		ObsConnection connection,
		string actionId,
		Dictionary<string, object>? parameters = null)
	{
		return ObsActions.Create(() => connection, new VariableApiAccessor())
			.Single(a => a.Id == actionId)
			.CreateExecutor()
			.ExecuteAsync(Context(parameters));
	}

	private static (ObsConnection Connection, FakeObsClient Client) Create(bool connected = true)
	{
		var client = new FakeObsClient { IsConnected = connected };
		return (new ObsConnection(client, "ws://localhost:4455", null), client);
	}

	[Test]
	public async Task Setting_the_preview_scene_with_studio_mode_off_says_so_instead_of_not_connected()
	{
		await using var server = new FakeObsWebSocketServer(requestType => requestType == "SetCurrentPreviewScene"
			? ObsRequestException.StudioModeNotActive
			: ObsRequestException.NotReady);
		await using var connection = new ObsConnection(new ObsClient(null, null, null, TimeSpan.FromSeconds(2)),
			server.Url,
			null,
			TimeSpan.FromMilliseconds(20),
			pollInterval: TimeSpan.FromMilliseconds(100));
		connection.Start();
		await WaitForAsync(() => connection.IsConnected);

		var result = await RunAsync(connection, "set-preview-scene", Scene("Live"));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
			Assert.That(result.ErrorMessage.Localized?.Key.ToString(),
				Does.EndWith("Integrations.Obs.Errors.StudioModeOff"));
			Assert.That(server.Requests, Does.Contain("SetCurrentPreviewScene"));
		});
	}

	[Test]
	public async Task A_command_obs_never_answers_fails_as_a_timeout()
	{
		await using var server = new FakeObsWebSocketServer(requestType
			=> requestType == "ToggleStream" ? null : ObsRequestException.NotReady);
		await using var connection = new ObsConnection(new ObsClient(null, null, null, TimeSpan.FromMilliseconds(500)),
			server.Url,
			null,
			TimeSpan.FromMilliseconds(20),
			pollInterval: TimeSpan.FromMilliseconds(100));
		connection.Start();
		await WaitForAsync(() => connection.IsConnected);

		var result = await RunAsync(connection, "toggle-streaming");

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Timeout));
			Assert.That(result.ErrorMessage.Localized?.Key.ToString(),
				Does.EndWith("Integrations.Obs.Errors.TimedOut"));
		});
	}

	[Test]
	public async Task Raising_the_volume_while_obs_never_answers_the_volume_read_fails_as_a_timeout()
	{
		await using var server = new FakeObsWebSocketServer(requestType
			=> requestType == "GetInputVolume" ? null : ObsRequestException.NotReady);
		await using var connection = new ObsConnection(new ObsClient(null, null, null, TimeSpan.FromMilliseconds(500)),
			server.Url,
			null,
			TimeSpan.FromMilliseconds(20),
			pollInterval: TimeSpan.FromMilliseconds(100));
		connection.Start();
		await WaitForAsync(() => connection.IsConnected);

		var result = await RunAsync(connection,
			"set-input-volume",
			new Dictionary<string, object>
			{
				[SetInputVolumeActionDefinition.InputParameter] = "Mic",
				[SetInputVolumeActionDefinition.ModeParameter] = "increase",
				[SetInputVolumeActionDefinition.VolumeParameter] = 10d
			});

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Timeout));
	}

	[Test]
	public async Task Setting_the_preview_scene_while_obs_accepts_it_succeeds()
	{
		var (connection, client) = Create();
		using (connection)
		{
			var result = await RunAsync(connection, "set-preview-scene", Scene("Live"));

			Assert.Multiple(() =>
			{
				Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
				Assert.That(client.Calls, Does.Contain("SetPreviewScene:Live"));
			});
		}
	}

	[Test]
	public async Task A_scene_obs_does_not_know_is_reported_as_obs_rejection_with_its_reason()
	{
		var (connection, client) = Create();
		client.RequestFailure = new ObsRequestException(ObsRequestException.ResourceNotFound,
			"ErrorCode: 600, Comment: No source was found by the name of Gone");
		using (connection)
		{
			var result = await RunAsync(connection, "set-scene", Scene("Gone"));

			Assert.Multiple(() =>
			{
				Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderRejected));
				Assert.That(result.ErrorMessage.Localized?.Arguments["message"]?.ToString(),
					Does.Contain("No source was found by the name of Gone"));
			});
		}
	}

	[Test]
	public async Task A_toggle_obs_refuses_is_reported_as_obs_rejection_not_as_not_connected()
	{
		var (connection, client) = Create();
		client.RequestFailure = new ObsRequestException(ObsRequestException.OutputNotRunning, "ErrorCode: 501");
		using (connection)
		{
			var result = await RunAsync(connection, "toggle-streaming");

			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderRejected));
		}
	}

	[Test]
	public async Task While_obs_is_offline_a_command_fails_with_not_connected_and_sends_nothing()
	{
		var (connection, client) = Create(false);
		using (connection)
		{
			var result = await RunAsync(connection, "set-preview-scene", Scene("Live"));

			Assert.Multiple(() =>
			{
				Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotConnected));
				Assert.That(client.Calls, Is.Empty);
			});
		}
	}

	[Test]
	public async Task A_connection_that_drops_during_a_command_fails_with_not_connected()
	{
		var (connection, client) = Create();
		client.DisconnectOnPreviewScene = true;
		using (connection)
		{
			var result = await RunAsync(connection, "set-preview-scene", Scene("Live"));

			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotConnected));
		}
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
