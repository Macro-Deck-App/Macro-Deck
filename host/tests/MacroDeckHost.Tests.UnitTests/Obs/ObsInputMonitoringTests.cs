using MacroDeckHost.Integrations.Obs.Actions;
using MacroDeckHost.Integrations.Obs.Connections;
using MacroDeckHost.Integrations.Obs.Protocol;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Tests.UnitTests.Obs;

[TestFixture]
internal sealed class ObsInputMonitoringTests
{
	private const string Mic = "Mic/Aux";

	private static (ObsConnection Connection, FakeObsClient Client) ConnectedConnection()
	{
		var client = new FakeObsClient { IsConnected = true };
		var connection = new ObsConnection(client, "ws://localhost:4455", null);
		return (connection, client);
	}

	private static IActionDefinition Action(Func<ObsConnection?> connection)
	{
		return ObsActions.Create(connection, new VariableApiAccessor())
			.Single(action => action.Id == "set-input-monitoring");
	}

	private static Task<ActionResult> RunAsync(Func<ObsConnection?> connection, Dictionary<string, object> parameters)
	{
		return Action(connection).CreateExecutor().ExecuteAsync(new ActionExecutionContext { Parameters = parameters });
	}

	private static Dictionary<string, object> Parameters(string? mode = null, string? monitorType = null)
	{
		var parameters = new Dictionary<string, object> { [SetInputMonitoringActionDefinition.InputParameter] = Mic };
		if (mode is not null)
		{
			parameters[SetInputMonitoringActionDefinition.ModeParameter] = mode;
		}

		if (monitorType is not null)
		{
			parameters[SetInputMonitoringActionDefinition.MonitorTypeParameter] = monitorType;
		}

		return parameters;
	}

	[TestCase("on", null, ObsMonitorTypes.None, ObsMonitorTypes.MonitorAndOutput)]
	[TestCase("on", "monitor-only", ObsMonitorTypes.None, ObsMonitorTypes.MonitorOnly)]
	[TestCase("on", "monitor-only", ObsMonitorTypes.MonitorAndOutput, ObsMonitorTypes.MonitorOnly)]
	[TestCase("off", null, ObsMonitorTypes.MonitorAndOutput, ObsMonitorTypes.None)]
	[TestCase("off", "monitor-only", ObsMonitorTypes.MonitorOnly, ObsMonitorTypes.None)]
	[TestCase(null, null, ObsMonitorTypes.None, ObsMonitorTypes.MonitorAndOutput)]
	[TestCase("toggle", "monitor-only", ObsMonitorTypes.None, ObsMonitorTypes.MonitorOnly)]
	[TestCase("toggle", null, ObsMonitorTypes.MonitorOnly, ObsMonitorTypes.None)]
	[TestCase("toggle", "monitor-only", ObsMonitorTypes.MonitorAndOutput, ObsMonitorTypes.None)]
	public async Task Sets_the_monitor_type_obs_ends_up_with(
		string? mode,
		string? monitorType,
		string before,
		string expected)
	{
		var (connection, client) = ConnectedConnection();
		using var _ = connection;
		client.InputAudioMonitorTypes[Mic] = before;

		var result = await RunAsync(() => connection, Parameters(mode, monitorType));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(client.InputAudioMonitorTypes[Mic], Is.EqualTo(expected));
		});
	}

	[Test]
	public async Task Without_an_input_it_fails_without_touching_obs()
	{
		var (connection, client) = ConnectedConnection();
		using var _ = connection;

		var result = await RunAsync(() => connection, new Dictionary<string, object>());

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(client.Calls, Has.None.StartsWith("SetInputAudioMonitorType"));
		});
	}

	[Test]
	public async Task Without_a_connection_it_fails_with_not_connected()
	{
		var result = await RunAsync(() => null, Parameters("on"));

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotConnected));
	}

	[Test]
	public async Task A_request_obs_refuses_fails_with_its_reason_instead_of_not_connected()
	{
		var (connection, client) = ConnectedConnection();
		using var _ = connection;
		client.RequestFailure = new ObsRequestException(604, "The specified input does not support audio.");

		var result = await RunAsync(() => connection, Parameters("on"));

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderRejected));
			Assert.That(result.ErrorMessage.Localized?.Arguments["message"]?.ToString(),
				Is.EqualTo("The specified input does not support audio."));
		});
	}

	[TestCase(ObsMonitorTypes.None, "not-monitoring")]
	[TestCase(ObsMonitorTypes.MonitorOnly, "monitoring")]
	[TestCase(ObsMonitorTypes.MonitorAndOutput, "monitoring")]
	public async Task The_button_state_shows_whether_the_input_is_monitored(string monitorType, string expectedState)
	{
		var (connection, client) = ConnectedConnection();
		using var _ = connection;
		client.InputAudioMonitorTypes[Mic] = monitorType;
		var provider = (IStateProviderActionDefinition)Action(() => connection);

		var state = await provider.GetActionStateAsync(
			new Dictionary<string, object?> { [SetInputMonitoringActionDefinition.InputParameter] = Mic },
			CancellationToken.None);

		Assert.That(state!.ActiveStateId, Is.EqualTo(expectedState));
	}

	[Test]
	public async Task The_button_state_is_unavailable_without_a_connection()
	{
		var provider = (IStateProviderActionDefinition)Action(() => null);

		var state = await provider.GetActionStateAsync(
			new Dictionary<string, object?> { [SetInputMonitoringActionDefinition.InputParameter] = Mic },
			CancellationToken.None);

		Assert.That(state!.ActiveStateId, Is.EqualTo("unavailable"));
	}

	[Test]
	public async Task The_button_state_read_right_after_a_press_shows_the_new_state()
	{
		var (connection, _) = ConnectedConnection();
		using var __ = connection;
		var action = Action(() => connection);
		var provider = (IStateProviderActionDefinition)action;
		var mic = new Dictionary<string, object?> { [SetInputMonitoringActionDefinition.InputParameter] = Mic };

		var before = await provider.GetActionStateAsync(mic, CancellationToken.None);
		await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext { Parameters = Parameters() });
		var after = await provider.GetActionStateAsync(mic, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(before!.ActiveStateId, Is.EqualTo("not-monitoring"));
			Assert.That(after!.ActiveStateId, Is.EqualTo("monitoring"));
		});
	}
}
