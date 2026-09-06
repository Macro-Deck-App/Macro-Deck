using System.Globalization;
using MacroDeckHost.Integrations.StreamlabsDesktop;
using MacroDeckHost.Integrations.StreamlabsDesktop.Actions;
using MacroDeckHost.Integrations.StreamlabsDesktop.Protocol;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Tests.UnitTests.StreamlabsDesktop;

[TestFixture]
public class StreamlabsDesktopActionsTests
{
	private static readonly string[] _expectedIds =
	[
		"set-scene",
		"start-streaming",
		"stop-streaming",
		"toggle-streaming",
		"start-recording",
		"stop-recording",
		"toggle-recording",
		"start-replay-buffer",
		"stop-replay-buffer",
		"toggle-replay-buffer",
		"save-replay",
		"set-source-visibility",
		"get-source-visibility",
		"set-audio-mute",
		"set-audio-volume",
		"get-audio-state",
		"scene-item-command",
		"toggle-studio-mode",
		"studio-mode-transition"
	];

	private readonly List<StreamlabsDesktopConnection> _connections = [];

	private VariableApiAccessor _variables = null!;
	private RecordingVariableApi _variableApi = null!;

	[SetUp]
	public void SetUp()
	{
		_variableApi = new RecordingVariableApi();
		_variables = new VariableApiAccessor { Current = _variableApi };
	}

	[TearDown]
	public void TearDown()
	{
		foreach (var connection in _connections)
		{
			connection.Dispose();
		}

		_connections.Clear();
	}

	[Test]
	public void TheActionSetIsTheFrozenList()
	{
		var actions = StreamlabsDesktopActions.Create(() => null, _variables);

		Assert.Multiple(() =>
		{
			Assert.That(actions.Select(action => action.Id), Is.EquivalentTo(_expectedIds));
			Assert.That(actions.Select(action => action.Id).Distinct().Count(),
				Is.EqualTo(actions.Count),
				"action ids must be unique");
			Assert.That(actions.Select(action => TestLocalization.Resolve(action.Name)), Is.All.Not.Empty);
			Assert.That(actions.Select(action => TestLocalization.Resolve(action.Description)),
				Is.All.Not.Empty);
		});
	}

	[Test]
	public async Task EveryActionFailsAsNotConnectedWithoutAConnection()
	{
		var actions = StreamlabsDesktopActions.Create(() => null, _variables);

		foreach (var action in actions)
		{
			var result = await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
			{
				["scene"] = "Gameplay",
				["source"] = "Webcam",
				["variable"] = "v",
				["volume"] = 50d
			}));

			Assert.That(result.ErrorCode,
				Is.EqualTo(ActionErrorCodes.NotConnected),
				$"{action.Id} should report a missing connection");
		}
	}

	[Test]
	public async Task SetScene_SwitchesToTheResolvedScene()
	{
		var client = StreamlabsJson.Seeded();
		var action = await FindAsync<SetSceneActionDefinition>(client);

		var result = await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
		{
			["scene"] = StreamlabsJson.SceneName
		}));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(client.Calls,
				Does.Contain(
					$"{StreamlabsServices.Scenes}.{StreamlabsServices.MakeSceneActive}:{StreamlabsJson.SceneId}"));
		});
	}

	[Test]
	public async Task SetScene_FailsAsNotFoundForAnUnknownScene()
	{
		var action = await FindAsync<SetSceneActionDefinition>(StreamlabsJson.Seeded());

		var result = await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
		{
			["scene"] = "Nope"
		}));

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
	}

	[Test]
	public async Task SetScene_FailsAsInvalidParameterWithNoSceneSelected()
	{
		var action = await FindAsync<SetSceneActionDefinition>(StreamlabsJson.Seeded());

		var result = await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>()));

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
	}

	[Test]
	public async Task SetScene_OffersTheLiveSceneList()
	{
		var action = await FindAsync<SetSceneActionDefinition>(StreamlabsJson.Seeded());

		var options = await action.GetDynamicOptionsAsync(new DynamicOptionsContext
				{ ParameterName = "scene", CurrentParameters = new Dictionary<string, object?>() },
			CancellationToken.None);

		Assert.That(options.Options.Select(option => option.Value),
			Is.EquivalentTo(new[] { StreamlabsJson.SceneName, StreamlabsJson.OtherSceneName }));
	}

	[TestCase("show", "True")]
	[TestCase("hide", "False")]
	[TestCase("toggle", "False")]
	public async Task SourceVisibility_MapsTheModeOntoSetVisibility(string mode, string expected)
	{
		var client = StreamlabsJson.Seeded();
		var action = await FindAsync<SourceVisibilityActionDefinition>(client);

		await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
		{
			["scene"] = StreamlabsJson.SceneName,
			["source"] = StreamlabsJson.CameraSourceName,
			["mode"] = mode
		}));

		Assert.That(client.Calls,
			Does.Contain($"{StreamlabsJson.SceneItemResource}.{StreamlabsServices.SetVisibility}:{expected}"));
	}

	[Test]
	public async Task SourceVisibility_SourceOptionsDependOnTheSelectedScene()
	{
		var action = await FindAsync<SourceVisibilityActionDefinition>(StreamlabsJson.Seeded());

		var withScene = await action.GetDynamicOptionsAsync(new DynamicOptionsContext
			{
				ParameterName = "source",
				CurrentParameters = new Dictionary<string, object?> { ["scene"] = StreamlabsJson.SceneName }
			},
			CancellationToken.None);

		var withoutScene = await action.GetDynamicOptionsAsync(new DynamicOptionsContext
			{
				ParameterName = "source",
				CurrentParameters = new Dictionary<string, object?>()
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(withScene.Options.Select(option => option.Value),
				Is.EquivalentTo(new[] { StreamlabsJson.CameraSourceName }));
			Assert.That(withoutScene.Options, Is.Empty);
		});
	}

	[TestCase("mute", "True")]
	[TestCase("unmute", "False")]
	[TestCase("toggle", "True")]
	public async Task MuteAudioSource_MapsTheModeOntoSetMuted(string mode, string expected)
	{
		var client = StreamlabsJson.Seeded();
		var action = await FindAsync<MuteAudioSourceActionDefinition>(client);

		await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
		{
			["source"] = StreamlabsJson.MicSourceName,
			["mode"] = mode
		}));

		Assert.That(client.Calls,
			Does.Contain($"{StreamlabsJson.MicResource}.{StreamlabsServices.SetMuted}:{expected}"));
	}

	[Test]
	public async Task MuteAudioSource_OffersTheAudioSourcesOnly()
	{
		var action = await FindAsync<MuteAudioSourceActionDefinition>(StreamlabsJson.Seeded());

		var options = await action.GetDynamicOptionsAsync(new DynamicOptionsContext
				{ ParameterName = "source", CurrentParameters = new Dictionary<string, object?>() },
			CancellationToken.None);

		Assert.That(options.Options.Select(option => option.Value),
			Is.EquivalentTo(new[] { StreamlabsJson.MicSourceName }));
	}

	[Test]
	public async Task SetAudioVolume_ParsesAVariableBackedValueInvariantly()
	{
		var original = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = new CultureInfo("de-DE");
		try
		{
			var client = StreamlabsJson.Seeded();
			var action = await FindAsync<SetAudioVolumeActionDefinition>(client);

			await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
			{
				["source"] = StreamlabsJson.MicSourceName,
				["mode"] = "set",
				["volume"] = "41.6"
			}));

			var deflection = client.Invocations
				.Single(invocation => invocation.Method == StreamlabsServices.SetDeflection)
				.Args[0];

			Assert.That((double)deflection!, Is.EqualTo(0.416d).Within(0.0001d));
		}
		finally
		{
			CultureInfo.CurrentCulture = original;
		}
	}

	[Test]
	public async Task SetAudioVolume_DecreaseReadsBeforeItWrites()
	{
		var client = StreamlabsJson.Seeded();
		var action = await FindAsync<SetAudioVolumeActionDefinition>(client);
		client.Calls.Clear();

		await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
		{
			["source"] = StreamlabsJson.MicSourceName,
			["mode"] = "decrease",
			["volume"] = 30d
		}));

		Assert.Multiple(() =>
		{
			Assert.That(client.Calls[0], Is.EqualTo($"{StreamlabsJson.MicResource}.{StreamlabsServices.GetModel}"));
			Assert.That(client.Calls,
				Does.Contain($"{StreamlabsJson.MicResource}.{StreamlabsServices.SetDeflection}:0.5"));
		});
	}

	[Test]
	public async Task GetAudioState_WritesTheVolumeAsANumericVariable()
	{
		var action = await FindAsync<GetAudioStateActionDefinition>(StreamlabsJson.Seeded());

		var result = await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
		{
			["source"] = StreamlabsJson.MicSourceName,
			["state"] = "volume",
			["variable"] = "mic_volume"
		}));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(_variableApi.Created["mic_volume"], Is.EqualTo(VariableType.Numeric));
			Assert.That(_variableApi.Values["mic_volume"], Is.EqualTo(80d).Within(0.01d));
		});
	}

	[Test]
	public async Task GetAudioState_WritesTheMuteAsABooleanVariable()
	{
		var client = StreamlabsJson.Seeded();
		client.Responses[$"{StreamlabsJson.MicResource}.{StreamlabsServices.GetModel}"] =
			StreamlabsJson.AudioSource(muted: true);

		var action = await FindAsync<GetAudioStateActionDefinition>(client);

		await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
		{
			["source"] = StreamlabsJson.MicSourceName,
			["state"] = "muted",
			["variable"] = "mic_muted"
		}));

		Assert.Multiple(() =>
		{
			Assert.That(_variableApi.Created["mic_muted"], Is.EqualTo(VariableType.Boolean));
			Assert.That(_variableApi.Values["mic_muted"], Is.EqualTo(true));
		});
	}

	[Test]
	public async Task GetSourceVisibility_WritesABooleanVariable()
	{
		var action = await FindAsync<GetSourceVisibilityActionDefinition>(StreamlabsJson.Seeded());

		await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
		{
			["scene"] = StreamlabsJson.SceneName,
			["source"] = StreamlabsJson.CameraSourceName,
			["variable"] = "cam_visible"
		}));

		Assert.Multiple(() =>
		{
			Assert.That(_variableApi.Created["cam_visible"], Is.EqualTo(VariableType.Boolean));
			Assert.That(_variableApi.Values["cam_visible"], Is.EqualTo(true));
		});
	}

	[Test]
	public async Task GetSourceVisibility_FailsAsNotFoundForAnUnknownSource()
	{
		var action = await FindAsync<GetSourceVisibilityActionDefinition>(StreamlabsJson.Seeded());

		var result = await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
		{
			["scene"] = StreamlabsJson.SceneName,
			["source"] = "Nope",
			["variable"] = "v"
		}));

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
	}

	[Test]
	public async Task SceneItemCommand_RunsTheTransformMethod()
	{
		var client = StreamlabsJson.Seeded();
		var action = await FindAsync<SceneItemCommandActionDefinition>(client);

		await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
		{
			["scene"] = StreamlabsJson.SceneName,
			["source"] = StreamlabsJson.CameraSourceName,
			["command"] = "fit-to-screen",
			["degrees"] = 90d
		}));

		Assert.That(client.Calls,
			Does.Contain($"{StreamlabsJson.SceneItemResource}.fitToScreen"),
			"a hidden parameter is still sent, so the degrees must be ignored here");
	}

	[Test]
	public async Task SceneItemCommand_PassesTheDegreesForRotate()
	{
		var client = StreamlabsJson.Seeded();
		var action = await FindAsync<SceneItemCommandActionDefinition>(client);

		await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
		{
			["scene"] = StreamlabsJson.SceneName,
			["source"] = StreamlabsJson.CameraSourceName,
			["command"] = "rotate",
			["degrees"] = 45d
		}));

		Assert.That(client.Calls, Does.Contain($"{StreamlabsJson.SceneItemResource}.rotate:45"));
	}

	[Test]
	public async Task SceneItemCommand_RejectsAnUnknownCommand()
	{
		var action = await FindAsync<SceneItemCommandActionDefinition>(StreamlabsJson.Seeded());

		var result = await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
		{
			["scene"] = StreamlabsJson.SceneName,
			["source"] = StreamlabsJson.CameraSourceName,
			["command"] = "explode"
		}));

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
	}

	[Test]
	public async Task ARefusedCall_SurfacesAsProviderRejected()
	{
		var client = StreamlabsJson.Seeded();
		client.Failures[$"{StreamlabsServices.Streaming}.{StreamlabsServices.SaveReplay}"] =
			new StreamlabsRpcException("Replay buffer is not running");

		var connection = await StartAsync(client);
		var action = StreamlabsDesktopActions.Create(() => connection, _variables)
			.First(candidate => candidate.Id == "save-replay");

		var result = await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>()));

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderRejected));
	}

	private static ActionExecutionContext Context(IReadOnlyDictionary<string, object> parameters)
		=> new() { Parameters = parameters };

	private async Task<T> FindAsync<T>(FakeStreamlabsClient client)
		where T : IActionDefinition
	{
		var connection = await StartAsync(client);
		return StreamlabsDesktopActions.Create(() => connection, _variables).OfType<T>().Single();
	}

	[Test]
	public async Task ToggleStreaming_ReportsWhetherStreamlabsIsLive()
	{
		var provider = await ProviderAsync(StreamlabsJson.Seeded(streaming: "live"), "toggle-streaming");

		var snapshot = await provider.GetActionStateAsync(_noParameters, CancellationToken.None);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("streaming"));
	}

	[Test]
	public async Task ToggleRecording_ReportsWhetherStreamlabsIsRecording()
	{
		var provider = await ProviderAsync(StreamlabsJson.Seeded(recording: "recording"), "toggle-recording");

		var snapshot = await provider.GetActionStateAsync(_noParameters, CancellationToken.None);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("recording"));
	}

	[Test]
	public async Task ToggleStudioMode_ReportsWhetherStudioModeIsOn()
	{
		var provider = await ProviderAsync(StreamlabsJson.Seeded(studioMode: true), "toggle-studio-mode");

		var snapshot = await provider.GetActionStateAsync(_noParameters, CancellationToken.None);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("on"));
	}

	[Test]
	public async Task SetScene_ReportsWhetherTheConfiguredSceneIsTheActiveOne()
	{
		var provider = await ProviderAsync(StreamlabsJson.Seeded(), "set-scene");

		var active = await provider.GetActionStateAsync(
			new Dictionary<string, object?> { ["scene"] = StreamlabsJson.SceneName },
			CancellationToken.None);
		var inactive = await provider.GetActionStateAsync(
			new Dictionary<string, object?> { ["scene"] = StreamlabsJson.OtherSceneName },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(active!.ActiveStateId, Is.EqualTo("active"));
			Assert.That(inactive!.ActiveStateId, Is.EqualTo("inactive"));
		});
	}

	[Test]
	public async Task AToggleIsUnavailableWhileStreamlabsIsNotConnected()
	{
		var provider = (IStateProviderActionDefinition)StreamlabsDesktopActions
			.Create(() => null, _variables)
			.Single(action => action.Id == "toggle-streaming");

		var snapshot = await provider.GetActionStateAsync(_noParameters, CancellationToken.None);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("unavailable"));
	}

	private static readonly Dictionary<string, object?> _noParameters = new(StringComparer.Ordinal);

	private async Task<IStateProviderActionDefinition> ProviderAsync(FakeStreamlabsClient client, string actionId)
	{
		var connection = await StartAsync(client);
		return (IStateProviderActionDefinition)StreamlabsDesktopActions
			.Create(() => connection, _variables)
			.Single(action => action.Id == actionId);
	}

	private async Task<StreamlabsDesktopConnection> StartAsync(FakeStreamlabsClient client)
	{
		var connection = new StreamlabsDesktopConnection(() => client,
			new StreamlabsDesktopEndpoint("127.0.0.1", 59650),
			"token",
			reconnectDelay: TimeSpan.FromMilliseconds(20));

		_connections.Add(connection);
		connection.Start();

		for (var attempt = 0; attempt < 200 && !connection.State.IsConnected; attempt++)
		{
			await Task.Delay(10);
		}

		Assert.That(connection.State.IsConnected, Is.True, "the fake session never connected");
		return connection;
	}

	private sealed class RecordingVariableApi : IVariableApi
	{
		public Dictionary<string, VariableType> Created { get; } = new(StringComparer.Ordinal);

		public Dictionary<string, object?> Values { get; } = new(StringComparer.Ordinal);

		private readonly Dictionary<Guid, string> _names = [];

		public Task<IReadOnlyList<VariableHandle>> GetAllAsync()
			=> Task.FromResult<IReadOnlyList<VariableHandle>>([]);

		public Task<VariableHandle?> GetByNameAsync(string name) => Task.FromResult<VariableHandle?>(null);

		public Task<VariableHandle> CreateAsync(
			string name,
			VariableType type,
			object? initialValue = null,
			int? decimalPlaces = null,
			string? definitionId = null)
		{
			var id = Guid.NewGuid();
			Created[name] = type;
			_names[id] = name;
			return Task.FromResult(new VariableHandle(id, name, type, initialValue, decimalPlaces)
			{
				DefinitionId = definitionId
			});
		}

		public Task SetValueAsync(Guid variableId, object? value)
		{
			Values[_names[variableId]] = value;
			return Task.CompletedTask;
		}

		public Task DeleteAsync(Guid variableId) => Task.CompletedTask;
	}
}
