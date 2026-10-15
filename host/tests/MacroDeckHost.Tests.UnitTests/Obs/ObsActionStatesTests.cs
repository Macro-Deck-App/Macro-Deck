using MacroDeckHost.Integrations.Obs.Actions;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.Obs.Connections;
using MacroDeckHost.Integrations.Obs.Protocol;
using MacroDeckHost.Integrations.Obs.Variables;

namespace MacroDeckHost.Tests.UnitTests.Obs;

/// <summary>
/// A button bound to an OBS toggle has to show what OBS is really doing, including after a change made
/// in OBS itself, and has to say "unavailable" rather than guess when OBS is not there (issue #741).
/// </summary>
[TestFixture]
internal sealed class ObsActionStatesTests
{
	private static FakeObsClient Running(ObsStatus? status = null)
	{
		return new FakeObsClient { IsConnected = true, Status = status ?? new ObsStatus() };
	}

	private static ObsConnection Connect(FakeObsClient client)
	{
		var connection = new ObsConnection(client, "ws://localhost:4455", null);
		if (client.IsConnected)
		{
			client.RaiseStateChanged();
		}

		return connection;
	}

	private static IStateProviderActionDefinition Toggle(string id, ObsConnection connection)
	{
		return (IStateProviderActionDefinition)ObsActions
			.Create(() => connection, new VariableApiAccessor())
			.Single(action => action.Id == id);
	}

	private static IActionDefinition Action(string id, ObsConnection connection)
	{
		return ObsActions.Create(() => connection, new VariableApiAccessor()).Single(action => action.Id == id);
	}

	private static readonly Dictionary<string, object?> _noParameters = new();

	private static readonly string[] _recordingStateIds = ["not-recording", "recording", "paused", "unavailable"];

	[Test]
	public async Task SetInputMute_ReadRightAfterPressingIt_ShowsTheNewState()
	{
		var client = Running();
		using var connection = Connect(client);
		var action = Action("set-input-mute", connection);
		var mic = new Dictionary<string, object?> { [MuteInputActionDefinition.InputParameter] = "Mic" };

		var before = await ((IStateProviderActionDefinition)action).GetActionStateAsync(mic, CancellationToken.None);
		client.MutedInputs["Mic"] = true;
		await action.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object> { [MuteInputActionDefinition.InputParameter] = "Mic" }
			});
		var after = await ((IStateProviderActionDefinition)action).GetActionStateAsync(mic, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(before!.ActiveStateId, Is.EqualTo("unmuted"));
			Assert.That(after!.ActiveStateId, Is.EqualTo("muted"));
		});
	}

	[Test]
	public async Task SetInputMute_FollowsAMuteMadeInObs_WithoutWaitingOutTheReadCache()
	{
		var client = Running();
		using var connection = Connect(client);
		var provider = (IStateProviderActionDefinition)Action("set-input-mute", connection);
		var mic = new Dictionary<string, object?> { [MuteInputActionDefinition.InputParameter] = "Mic" };

		var before = await provider.GetActionStateAsync(mic, CancellationToken.None);
		client.MutedInputs["Mic"] = true;
		client.RaiseInputMuteChanged("Mic", true);
		var after = await provider.GetActionStateAsync(mic, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(before!.ActiveStateId, Is.EqualTo("unmuted"));
			Assert.That(after!.ActiveStateId, Is.EqualTo("muted"));
		});
	}

	[Test]
	public async Task SetProfile_FollowsAProfileSwitchMadeInObs()
	{
		var client = Running(new ObsStatus { CurrentProfile = "Streaming" });
		using var connection = Connect(client);
		var provider = (IStateProviderActionDefinition)Action("set-profile", connection);
		var streaming = new Dictionary<string, object?> { [ProfileActionDefinition.ProfileParameter] = "Streaming" };

		var before = await provider.GetActionStateAsync(streaming, CancellationToken.None);
		client.Status = new ObsStatus { CurrentProfile = "Recording" };
		client.RaiseStateChanged();
		var after = await provider.GetActionStateAsync(streaming, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(before!.ActiveStateId, Is.EqualTo("active"));
			Assert.That(after!.ActiveStateId, Is.EqualTo("inactive"));
			Assert.That(ObsVariables.Read(connection.State, "current_profile"), Is.EqualTo("Recording"));
		});
	}

	[Test]
	public async Task SetProfile_WithNoProfileChosenYet_HasNothingToSay()
	{
		using var connection = Connect(Running(new ObsStatus { CurrentProfile = "Streaming" }));
		var provider = (IStateProviderActionDefinition)Action("set-profile", connection);

		var snapshot = await provider.GetActionStateAsync(_noParameters, CancellationToken.None);

		Assert.That(snapshot, Is.Null);
	}

	[Test]
	public async Task ToggleRecording_ReportsWhetherObsIsRecording()
	{
		using var connection = Connect(Running(new ObsStatus { IsRecording = true }));

		var snapshot = await Toggle("toggle-recording", connection)
			.GetActionStateAsync(_noParameters, CancellationToken.None);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("recording"));
	}

	[Test]
	public async Task ToggleRecording_SeparatesPausedFromRecording()
	{
		using var connection = Connect(Running(new ObsStatus { IsRecording = true, RecordingPaused = true }));

		var snapshot = await Toggle("toggle-recording", connection)
			.GetActionStateAsync(_noParameters, CancellationToken.None);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("paused"));
	}

	[Test]
	public async Task ToggleRecording_FollowsObsWhenRecordingStopsOutsideMacroDeck()
	{
		var client = Running(new ObsStatus { IsRecording = true });
		using var connection = Connect(client);
		var provider = Toggle("toggle-recording", connection);

		client.Status = new ObsStatus { IsRecording = false };
		client.RaiseStateChanged();
		var snapshot = await provider.GetActionStateAsync(_noParameters, CancellationToken.None);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("not-recording"));
	}

	[Test]
	public async Task ToggleStreaming_ReportsWhetherObsIsStreaming()
	{
		using var connection = Connect(Running(new ObsStatus { IsStreaming = true }));

		var snapshot = await Toggle("toggle-streaming", connection)
			.GetActionStateAsync(_noParameters, CancellationToken.None);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("streaming"));
	}

	[Test]
	public async Task ToggleStudioMode_ReportsWhetherStudioModeIsOn()
	{
		using var connection = Connect(Running(new ObsStatus { StudioModeActive = true }));

		var snapshot = await Toggle("toggle-studio-mode", connection)
			.GetActionStateAsync(_noParameters, CancellationToken.None);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("on"));
	}

	[Test]
	public async Task Toggle_WithoutAConnectedObs_IsUnavailableRatherThanOff()
	{
		var client = new FakeObsClient { IsConnected = false };
		using var connection = new ObsConnection(client, "ws://localhost:4455", null);

		var snapshot = await Toggle("toggle-recording", connection)
			.GetActionStateAsync(_noParameters, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(snapshot!.ActiveStateId, Is.EqualTo("unavailable"));
			Assert.That(snapshot.States.Select(state => state.Id),
				Is.EquivalentTo(_recordingStateIds));
		});
	}

	[Test]
	public async Task SetScene_ReportsWhetherTheConfiguredSceneIsTheCurrentOne()
	{
		using var connection = Connect(Running(new ObsStatus { CurrentScene = "Gameplay" }));
		var provider = (IStateProviderActionDefinition)Action("set-scene", connection);

		var active = await provider.GetActionStateAsync(
			new Dictionary<string, object?> { [SceneActionDefinition.SceneParameter] = "Gameplay" },
			CancellationToken.None);
		var inactive = await provider.GetActionStateAsync(
			new Dictionary<string, object?> { [SceneActionDefinition.SceneParameter] = "Intro" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(active!.ActiveStateId, Is.EqualTo("active"));
			Assert.That(inactive!.ActiveStateId, Is.EqualTo("inactive"));
		});
	}

	[Test]
	public async Task SetPreviewScene_ReadsThePreviewSceneRatherThanTheProgramOne()
	{
		using var connection = Connect(Running(new ObsStatus { CurrentScene = "Gameplay", PreviewScene = "Intro" }));
		var provider = (IStateProviderActionDefinition)Action("set-preview-scene", connection);

		var snapshot = await provider.GetActionStateAsync(
			new Dictionary<string, object?> { [SceneActionDefinition.SceneParameter] = "Intro" },
			CancellationToken.None);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("active"));
	}

	[Test]
	public async Task SetScene_WithNoSceneChosenYet_HasNothingToSay()
	{
		using var connection = Connect(Running(new ObsStatus { CurrentScene = "Gameplay" }));
		var provider = (IStateProviderActionDefinition)Action("set-scene", connection);

		var snapshot = await provider.GetActionStateAsync(new Dictionary<string, object?>(), CancellationToken.None);

		Assert.That(snapshot, Is.Null);
	}

	[Test]
	public async Task SetInputMute_ReportsTheInputsRealMuteState()
	{
		var client = Running(new ObsStatus());
		using var connection = Connect(client);
		client.MutedInputs["Mic/Aux"] = true;
		var provider = (IStateProviderActionDefinition)Action("set-input-mute", connection);

		var snapshot = await provider.GetActionStateAsync(
			new Dictionary<string, object?> { [MuteInputActionDefinition.InputParameter] = "Mic/Aux" },
			CancellationToken.None);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("muted"));
	}

	[Test]
	public async Task SetInputMute_ReadsOnceForSeveralButtonsOnTheSameInput()
	{
		var client = Running(new ObsStatus());
		using var connection = Connect(client);
		var provider = (IStateProviderActionDefinition)Action("set-input-mute", connection);
		var parameters = new Dictionary<string, object?> { [MuteInputActionDefinition.InputParameter] = "Mic/Aux" };

		await provider.GetActionStateAsync(parameters, CancellationToken.None);
		await provider.GetActionStateAsync(parameters, CancellationToken.None);
		await provider.GetActionStateAsync(parameters, CancellationToken.None);

		Assert.That(client.Calls.Count(call => call == "GetInputMuted:Mic/Aux"), Is.EqualTo(1));
	}

	[Test]
	public async Task SetSourceVisibility_ReportsWhetherTheSourceIsVisible()
	{
		var client = Running(new ObsStatus());
		using var connection = Connect(client);
		client.VisibleSources["Gameplay:Webcam"] = true;
		var provider = (IStateProviderActionDefinition)Action("set-source-visibility", connection);

		var snapshot = await provider.GetActionStateAsync(new Dictionary<string, object?>
			{
				[SourceVisibilityActionDefinition.SceneParameter] = "Gameplay",
				[SourceVisibilityActionDefinition.SourceParameter] = "Webcam"
			},
			CancellationToken.None);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("visible"));
	}

	[Test]
	public async Task SetSourceVisibility_WithOnlyHalfTheTargetChosen_HasNothingToSay()
	{
		using var connection = Connect(Running(new ObsStatus()));
		var provider = (IStateProviderActionDefinition)Action("set-source-visibility", connection);

		var snapshot = await provider.GetActionStateAsync(
			new Dictionary<string, object?> { [SourceVisibilityActionDefinition.SceneParameter] = "Gameplay" },
			CancellationToken.None);

		Assert.That(snapshot, Is.Null);
	}

	[Test]
	public async Task SetSourceFilter_ReportsWhetherTheFilterIsEnabled()
	{
		var client = Running(new ObsStatus());
		using var connection = Connect(client);
		client.FilterStates["Webcam::Blur"] = true;
		var provider = (IStateProviderActionDefinition)Action("set-source-filter", connection);

		var snapshot = await provider.GetActionStateAsync(new Dictionary<string, object?>
			{
				[SetSourceFilterActionDefinition.SourceParameter] = "Webcam",
				[SetSourceFilterActionDefinition.FilterParameter] = "Blur"
			},
			CancellationToken.None);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("enabled"));
	}

	[Test]
	public void PlainCommands_DoNotClaimToProvideStates()
	{
		using var connection = Connect(Running(new ObsStatus()));

		Assert.Multiple(() =>
		{
			Assert.That(Action("start-recording", connection), Is.Not.InstanceOf<IStateProviderActionDefinition>());
			Assert.That(Action("save-replay-buffer", connection), Is.Not.InstanceOf<IStateProviderActionDefinition>());
		});
	}
}
