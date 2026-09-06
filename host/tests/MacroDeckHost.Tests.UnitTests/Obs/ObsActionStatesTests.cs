using MacroDeckHost.Integrations.Obs;
using MacroDeckHost.Integrations.Obs.Actions;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Tests.UnitTests.Obs;

/// <summary>
/// A button bound to an OBS toggle has to show what OBS is really doing, including after a change made
/// in OBS itself, and has to say "unavailable" rather than guess when OBS is not there (issue #741).
/// </summary>
[TestFixture]
internal sealed class ObsActionStatesTests
{
	private static FakeObsClient Running(ObsStatus? status = null)
		=> new() { IsConnected = true, Status = status ?? new ObsStatus() };

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
		=> (IStateProviderActionDefinition)ObsActions
			.Create(() => connection, new VariableApiAccessor())
			.Single(action => action.Id == id);

	private static IActionDefinition Action(string id, ObsConnection connection)
		=> ObsActions.Create(() => connection, new VariableApiAccessor()).Single(action => action.Id == id);

	private static readonly Dictionary<string, object?> _noParameters = new();

	private static readonly string[] _recordingStateIds = ["not-recording", "recording", "paused", "unavailable"];

	[Test]
	public async Task ToggleRecording_ReportsWhetherObsIsRecording()
	{
		using var connection = Connect(Running(new ObsStatus { IsRecording = true }));

		var snapshot = await Toggle("toggle-recording", connection).GetActionStateAsync(_noParameters, default);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("recording"));
	}

	[Test]
	public async Task ToggleRecording_SeparatesPausedFromRecording()
	{
		using var connection = Connect(Running(new ObsStatus { IsRecording = true, RecordingPaused = true }));

		var snapshot = await Toggle("toggle-recording", connection).GetActionStateAsync(_noParameters, default);

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
		var snapshot = await provider.GetActionStateAsync(_noParameters, default);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("not-recording"));
	}

	[Test]
	public async Task ToggleStreaming_ReportsWhetherObsIsStreaming()
	{
		using var connection = Connect(Running(new ObsStatus { IsStreaming = true }));

		var snapshot = await Toggle("toggle-streaming", connection).GetActionStateAsync(_noParameters, default);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("streaming"));
	}

	[Test]
	public async Task ToggleStudioMode_ReportsWhetherStudioModeIsOn()
	{
		using var connection = Connect(Running(new ObsStatus { StudioModeActive = true }));

		var snapshot = await Toggle("toggle-studio-mode", connection).GetActionStateAsync(_noParameters, default);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("on"));
	}

	[Test]
	public async Task Toggle_WithoutAConnectedObs_IsUnavailableRatherThanOff()
	{
		var client = new FakeObsClient { IsConnected = false };
		using var connection = new ObsConnection(client, "ws://localhost:4455", null);

		var snapshot = await Toggle("toggle-recording", connection).GetActionStateAsync(_noParameters, default);

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
			default);
		var inactive = await provider.GetActionStateAsync(
			new Dictionary<string, object?> { [SceneActionDefinition.SceneParameter] = "Intro" },
			default);

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
			default);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("active"));
	}

	[Test]
	public async Task SetScene_WithNoSceneChosenYet_HasNothingToSay()
	{
		using var connection = Connect(Running(new ObsStatus { CurrentScene = "Gameplay" }));
		var provider = (IStateProviderActionDefinition)Action("set-scene", connection);

		var snapshot = await provider.GetActionStateAsync(new Dictionary<string, object?>(), default);

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
			default);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("muted"));
	}

	[Test]
	public async Task SetInputMute_ReadsOnceForSeveralButtonsOnTheSameInput()
	{
		var client = Running(new ObsStatus());
		using var connection = Connect(client);
		var provider = (IStateProviderActionDefinition)Action("set-input-mute", connection);
		var parameters = new Dictionary<string, object?> { [MuteInputActionDefinition.InputParameter] = "Mic/Aux" };

		await provider.GetActionStateAsync(parameters, default);
		await provider.GetActionStateAsync(parameters, default);
		await provider.GetActionStateAsync(parameters, default);

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
			default);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("visible"));
	}

	[Test]
	public async Task SetSourceVisibility_WithOnlyHalfTheTargetChosen_HasNothingToSay()
	{
		using var connection = Connect(Running(new ObsStatus()));
		var provider = (IStateProviderActionDefinition)Action("set-source-visibility", connection);

		var snapshot = await provider.GetActionStateAsync(
			new Dictionary<string, object?> { [SourceVisibilityActionDefinition.SceneParameter] = "Gameplay" },
			default);

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
			default);

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
