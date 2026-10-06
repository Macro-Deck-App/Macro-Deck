using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.Obs;
using MacroDeckHost.Integrations.Obs.Actions;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using DomainVariableType = MacroDeckHost.Domain.Enums.VariableType;

namespace MacroDeckHost.Tests.UnitTests.Obs;

[TestFixture]
internal sealed class ObsOutputActionsTests
{
	private static readonly string[] _outputIds = ["start-output", "stop-output", "toggle-output", "get-output-state"];
	private static readonly string[] _pluginOutputs = ["adv_stream", "multi_rtmp_1", "backtrack"];

	private static (ObsConnection Connection, FakeObsClient Client) Connected(bool connected = true)
	{
		var client = new FakeObsClient { IsConnected = connected, OutputNames = _pluginOutputs };
		return (new ObsConnection(client, "ws://localhost:4455", null), client);
	}

	private static IActionDefinition Output(string id, ObsConnection connection)
		=> ObsActions.Create(ObsTargetResolver.Legacy(() => connection), new VariableApiAccessor())
			.Single(action => action.Id == id);

	private static ActionExecutionContext Run(string? output)
		=> new()
		{
			Parameters = output is null
				? new Dictionary<string, object>()
				: new Dictionary<string, object> { [ObsOutputSupport.OutputParameter] = output }
		};

	[Test]
	public void The_catalog_offers_the_four_output_actions()
	{
		var (connection, _) = Connected();
		using (connection)
		{
			var ids = ObsActions.Create(() => connection, new VariableApiAccessor()).Select(action => action.Id);

			Assert.That(ids, Is.SupersetOf(_outputIds));
		}
	}

	[TestCase("start-output", "StartOutput:multi_rtmp_1")]
	[TestCase("stop-output", "StopOutput:multi_rtmp_1")]
	[TestCase("toggle-output", "ToggleOutput:multi_rtmp_1")]
	public async Task An_output_action_reaches_the_chosen_output_by_name(string actionId, string expectedCall)
	{
		var (connection, client) = Connected();
		ActionResult result;
		using (connection)
		{
			result = await Output(actionId, connection).CreateExecutor().ExecuteAsync(Run("multi_rtmp_1"));
		}

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(client.Calls, Does.Contain(expectedCall));
		});
	}

	[TestCase("start-output")]
	[TestCase("stop-output")]
	[TestCase("toggle-output")]
	[TestCase("get-output-state")]
	public async Task A_disconnected_obs_is_reported_and_nothing_is_sent(string actionId)
	{
		var (connection, client) = Connected(connected: false);
		ActionResult result;
		using (connection)
		{
			result = await Output(actionId, connection).CreateExecutor().ExecuteAsync(Run("multi_rtmp_1"));
		}

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotConnected));
			Assert.That(client.Calls.Where(call => call != "Disconnect"), Is.Empty);
		});
	}

	[TestCase("start-output")]
	[TestCase("stop-output")]
	[TestCase("toggle-output")]
	public async Task A_stored_output_that_no_longer_exists_fails_as_not_found(string actionId)
	{
		var (connection, _) = Connected();
		ActionResult result;
		using (connection)
		{
			result = await Output(actionId, connection).CreateExecutor().ExecuteAsync(Run("gone_output"));
		}

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
		});
	}

	[Test]
	public async Task An_obs_without_output_requests_is_not_reported_as_a_missing_output()
	{
		var (connection, client) = Connected();
		client.OutputFailure = new ObsRequestUnsupportedException("StartOutput");
		ActionResult result;
		using (connection)
		{
			result = await Output("start-output", connection).CreateExecutor().ExecuteAsync(Run("multi_rtmp_1"));
		}

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderError));
		});
	}

	[Test]
	public async Task A_failing_output_request_is_a_provider_error_not_a_missing_output()
	{
		var (connection, client) = Connected();
		client.OutputFailure = new InvalidOperationException("output is already running");
		ActionResult result;
		using (connection)
		{
			result = await Output("start-output", connection).CreateExecutor().ExecuteAsync(Run("multi_rtmp_1"));
		}

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderError));
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("   ")]
	public async Task Running_without_a_chosen_output_is_an_invalid_parameter(string? output)
	{
		var (connection, client) = Connected();
		ActionResult result;
		using (connection)
		{
			result = await Output("toggle-output", connection).CreateExecutor().ExecuteAsync(Run(output));
		}

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(client.Calls.Where(call => call != "Disconnect"), Is.Empty);
		});
	}

	[Test]
	public async Task The_picker_lists_the_outputs_obs_reports()
	{
		var (connection, _) = Connected();
		DynamicOptionsResult options;
		using (connection)
		{
			options = await Picker(connection, stored: null);
		}

		Assert.That(options.Options.Select(option => option.Value), Is.EqualTo(_pluginOutputs));
	}

	[Test]
	public async Task The_picker_keeps_a_stored_output_that_is_currently_missing()
	{
		var (connection, _) = Connected();
		DynamicOptionsResult options;
		using (connection)
		{
			options = await Picker(connection, stored: "gone_output");
		}

		var kept = options.Options.Single(option => option.Value == "gone_output");
		Assert.Multiple(() =>
		{
			Assert.That(options.Options, Has.Count.EqualTo(_pluginOutputs.Length + 1));
			Assert.That(kept.Label, Is.Not.EqualTo((LocalizedText)"gone_output"));
		});
	}

	[Test]
	public async Task The_picker_keeps_the_stored_output_when_obs_is_disconnected()
	{
		var (connection, _) = Connected(connected: false);
		DynamicOptionsResult options;
		using (connection)
		{
			options = await Picker(connection, stored: "multi_rtmp_1");
		}

		Assert.That(options.Options.Select(option => option.Value), Is.EqualTo(new[] { "multi_rtmp_1" }));
	}

	[Test]
	public async Task The_picker_survives_an_obs_without_output_requests()
	{
		var (connection, client) = Connected();
		client.OutputFailure = new ObsRequestUnsupportedException("GetOutputList");
		DynamicOptionsResult options;
		using (connection)
		{
			options = await Picker(connection, stored: "multi_rtmp_1");
		}

		Assert.That(options.Options.Select(option => option.Value), Is.EqualTo(new[] { "multi_rtmp_1" }));
	}

	[TestCase(true, "on")]
	[TestCase(false, "off")]
	public async Task The_toggle_action_follows_the_output_state(bool active, string expectedStateId)
	{
		var (connection, client) = Connected();
		client.OutputStates["multi_rtmp_1"] = active;
		ActionStateSnapshot? snapshot;
		using (connection)
		{
			snapshot = await State(connection, "multi_rtmp_1");
		}

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo(expectedStateId));
	}

	[Test]
	public async Task The_toggle_action_state_is_unavailable_when_the_output_cannot_be_read()
	{
		var (connection, _) = Connected();
		ActionStateSnapshot? snapshot;
		using (connection)
		{
			snapshot = await State(connection, "gone_output");
		}

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("unavailable"));
	}

	[Test]
	public async Task The_toggle_action_offers_no_state_until_an_output_is_chosen()
	{
		var (connection, _) = Connected();
		ActionStateSnapshot? snapshot;
		using (connection)
		{
			snapshot = await State(connection, "");
		}

		Assert.That(snapshot, Is.Null);
	}

	[TestCase(true)]
	[TestCase(false)]
	public async Task Get_output_state_writes_the_active_state_to_a_variable(bool active)
	{
		using var targets = new ActionVariableTargets(ObsIntegration.IntegrationId);
		var accessor = new VariableApiAccessor
		{
			Current = targets.IntegrationVariables,
			UserVariables = targets.UserVariables
		};
		var (connection, client) = Connected();
		client.OutputStates["multi_rtmp_1"] = active;
		ActionResult result;
		using (connection)
		{
			var action = new GetOutputStateActionDefinition(ObsTargetResolver.Legacy(() => connection), accessor);
			result = await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object>
				{
					[ObsOutputSupport.OutputParameter] = "multi_rtmp_1",
					[GetOutputStateActionDefinition.VariableParameter] = "rtmp_live"
				}
			});
		}

		var variable = await targets.Find("rtmp_live");
		Assert.Multiple(async () =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(await targets.ValueOf("rtmp_live"), Is.EqualTo(active));
			Assert.That(variable!.Type, Is.EqualTo(DomainVariableType.Boolean));
		});
	}

	[Test]
	public async Task Get_output_state_of_a_missing_output_fails_as_not_found_and_writes_nothing()
	{
		using var targets = new ActionVariableTargets(ObsIntegration.IntegrationId);
		var accessor = new VariableApiAccessor
		{
			Current = targets.IntegrationVariables,
			UserVariables = targets.UserVariables
		};
		var (connection, _) = Connected();
		ActionResult result;
		using (connection)
		{
			var action = new GetOutputStateActionDefinition(ObsTargetResolver.Legacy(() => connection), accessor);
			result = await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object>
				{
					[ObsOutputSupport.OutputParameter] = "gone_output",
					[GetOutputStateActionDefinition.VariableParameter] = "rtmp_live"
				}
			});
		}

		Assert.Multiple(async () =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
			Assert.That(await targets.Find("rtmp_live"), Is.Null);
		});
	}

	private static Task<DynamicOptionsResult> Picker(ObsConnection connection, string? stored)
		=> ((IDynamicOptionsActionDefinition)Output("start-output", connection)).GetDynamicOptionsAsync(
			new DynamicOptionsContext
			{
				ParameterName = ObsOutputSupport.OutputParameter,
				CurrentParameters = stored is null
					? new Dictionary<string, object?>()
					: new Dictionary<string, object?> { [ObsOutputSupport.OutputParameter] = stored }
			},
			CancellationToken.None);

	private static Task<ActionStateSnapshot?> State(ObsConnection connection, string output)
		=> ((IStateProviderActionDefinition)Output("toggle-output", connection)).GetActionStateAsync(
			new Dictionary<string, object?> { [ObsOutputSupport.OutputParameter] = output },
			CancellationToken.None);
}
