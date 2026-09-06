using System.Text.Json;
using MacroDeckHost.Integrations.Obs;
using MacroDeckHost.Integrations.Obs.Actions;
using MacroDeckHost.Tests.UnitTests.System;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Tests.UnitTests.Obs;

[TestFixture]
internal sealed class ObsActionsTests
{
	private static readonly string[] _expectedSceneOptions = ["Intro", "Gameplay", "Outro"];
	private static readonly string[] _expectedSourceOptions = ["Webcam", "Capture"];
	private static readonly string[] _expectedFilterOptions = ["Blur", "Color Correction"];
	private static readonly string[] _expectedSourceNameOptions = ["Gameplay", "Webcam", "Mic/Aux"];

	private static ActionExecutionContext Context(Dictionary<string, object> parameters)
		=> new() { Parameters = parameters };

	private static (ObsConnection Connection, FakeObsClient Client) ConnectedConnection()
	{
		var client = new FakeObsClient { IsConnected = true };
		var connection = new ObsConnection(client, "ws://localhost:4455", null);
		return (connection, client);
	}

	[Test]
	public async Task SetScene_ExecutesSetCurrentScene()
	{
		var (connection, client) = ConnectedConnection();
		using (connection)
		{
			var action = new SceneActionDefinition("set-scene", "Set Scene", "", preview: false, () => connection);

			await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
				{ [SceneActionDefinition.SceneParameter] = "Gameplay" }));
		}

		Assert.That(client.Calls, Does.Contain("SetCurrentScene:Gameplay"));
	}

	[Test]
	public async Task SetScene_DynamicOptions_ReturnsLiveSceneList()
	{
		var (connection, client) = ConnectedConnection();
		client.SceneNames = ["Intro", "Gameplay", "Outro"];
		using (connection)
		{
			var action = new SceneActionDefinition("set-scene", "Set Scene", "", preview: false, () => connection);

			var result = await action.GetDynamicOptionsAsync(new DynamicOptionsContext
				{
					ParameterName = SceneActionDefinition.SceneParameter,
					CurrentParameters = new Dictionary<string, object?>()
				},
				CancellationToken.None);

			Assert.That(result.Options.Select(o => o.Value), Is.EqualTo(_expectedSceneOptions));
		}
	}

	[Test]
	public async Task ToggleRecording_ExecutesToggleRecord()
	{
		var (connection, client) = ConnectedConnection();
		using (connection)
		{
			var actions = ObsActions.Create(() => connection, new VariableApiAccessor());
			var toggle = actions.Single(a => a.Id == "toggle-recording");

			await toggle.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>()));
		}

		Assert.That(client.Calls, Does.Contain("ToggleRecord"));
	}

	[Test]
	public async Task ToggleRecording_WithNoConnection_FailsWithNotConnected()
	{
		var actions = ObsActions.Create(() => null, new VariableApiAccessor());
		var toggle = actions.Single(a => a.Id == "toggle-recording");

		var result = await toggle.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>()));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotConnected));
		});
	}

	[Test]
	public async Task SourceVisibility_SourceOptions_DependOnSelectedScene()
	{
		var (connection, client) = ConnectedConnection();
		client.SceneItems["Gameplay"] = ["Webcam", "Capture"];
		using (connection)
		{
			var action = new SourceVisibilityActionDefinition(() => connection);

			var result = await action.GetDynamicOptionsAsync(new DynamicOptionsContext
				{
					ParameterName = SourceVisibilityActionDefinition.SourceParameter,
					CurrentParameters = new Dictionary<string, object?>
					{
						[SourceVisibilityActionDefinition.SceneParameter] = "Gameplay"
					}
				},
				CancellationToken.None);

			Assert.That(result.Options.Select(o => o.Value), Is.EqualTo(_expectedSourceOptions));
		}
	}

	[Test]
	public async Task SourceVisibility_HideMode_ExecutesSetSourceVisibleFalse()
	{
		var (connection, client) = ConnectedConnection();
		using (connection)
		{
			var action = new SourceVisibilityActionDefinition(() => connection);

			await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
			{
				[SourceVisibilityActionDefinition.SceneParameter] = "Gameplay",
				[SourceVisibilityActionDefinition.SourceParameter] = "Webcam",
				[SourceVisibilityActionDefinition.ModeParameter] = "hide"
			}));
		}

		Assert.That(client.Calls, Does.Contain("SetSourceVisible:Gameplay:Webcam:False"));
	}

	[Test]
	public async Task MuteInput_ExecutesToggleByDefault()
	{
		var (connection, client) = ConnectedConnection();
		using (connection)
		{
			var action = new MuteInputActionDefinition(() => connection);

			await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
			{
				[MuteInputActionDefinition.InputParameter] = "Mic/Aux"
			}));
		}

		Assert.That(client.Calls, Does.Contain("ToggleInputMute:Mic/Aux"));
	}

	[Test]
	public async Task SetInputVolume_SetMode_WritesMultiplierFromPercent()
	{
		var (connection, client) = ConnectedConnection();
		using (connection)
		{
			var action = new SetInputVolumeActionDefinition(() => connection);

			await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
			{
				[SetInputVolumeActionDefinition.InputParameter] = "Mic/Aux",
				[SetInputVolumeActionDefinition.ModeParameter] = "set",
				[SetInputVolumeActionDefinition.VolumeParameter] = 50
			}));
		}

		Assert.That(client.Calls, Does.Contain("SetInputVolume:Mic/Aux:0.5"));
	}

	[Test]
	public async Task SetInputVolume_IncreaseMode_AddsToCurrentVolume()
	{
		var (connection, client) = ConnectedConnection();
		client.InputVolumes["Mic/Aux"] = 0.5f;
		using (connection)
		{
			var action = new SetInputVolumeActionDefinition(() => connection);

			await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
			{
				[SetInputVolumeActionDefinition.InputParameter] = "Mic/Aux",
				[SetInputVolumeActionDefinition.ModeParameter] = "increase",
				[SetInputVolumeActionDefinition.VolumeParameter] = 10
			}));
		}

		Assert.That(client.Calls, Does.Contain("SetInputVolume:Mic/Aux:0.6"));
	}

	[Test]
	public async Task SetInputVolume_DecreaseMode_ClampsAtZero()
	{
		var (connection, client) = ConnectedConnection();
		client.InputVolumes["Mic/Aux"] = 0.05f;
		using (connection)
		{
			var action = new SetInputVolumeActionDefinition(() => connection);

			await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
			{
				[SetInputVolumeActionDefinition.InputParameter] = "Mic/Aux",
				[SetInputVolumeActionDefinition.ModeParameter] = "decrease",
				[SetInputVolumeActionDefinition.VolumeParameter] = 20
			}));
		}

		Assert.That(client.Calls, Does.Contain("SetInputVolume:Mic/Aux:0"));
	}

	[Test]
	public async Task SetSourceFilter_EnableMode_ExecutesSetSourceFilterEnabledTrue()
	{
		var (connection, client) = ConnectedConnection();
		using (connection)
		{
			var action = new SetSourceFilterActionDefinition(() => connection);

			await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
			{
				[SetSourceFilterActionDefinition.SourceParameter] = "Webcam",
				[SetSourceFilterActionDefinition.FilterParameter] = "Blur",
				[SetSourceFilterActionDefinition.ModeParameter] = "enable"
			}));
		}

		Assert.That(client.Calls, Does.Contain("SetSourceFilterEnabled:Webcam:Blur:True"));
	}

	[Test]
	public async Task SetSourceFilter_ExecutesToggleByDefault()
	{
		var (connection, client) = ConnectedConnection();
		using (connection)
		{
			var action = new SetSourceFilterActionDefinition(() => connection);

			await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
			{
				[SetSourceFilterActionDefinition.SourceParameter] = "Webcam",
				[SetSourceFilterActionDefinition.FilterParameter] = "Blur"
			}));
		}

		Assert.That(client.Calls, Does.Contain("ToggleSourceFilterEnabled:Webcam:Blur"));
	}

	[Test]
	public async Task SetSourceFilter_FilterOptions_DependOnSelectedSource()
	{
		var (connection, client) = ConnectedConnection();
		client.SourceFilters["Webcam"] = ["Blur", "Color Correction"];
		using (connection)
		{
			var action = new SetSourceFilterActionDefinition(() => connection);

			var result = await action.GetDynamicOptionsAsync(new DynamicOptionsContext
				{
					ParameterName = SetSourceFilterActionDefinition.FilterParameter,
					CurrentParameters = new Dictionary<string, object?>
					{
						[SetSourceFilterActionDefinition.SourceParameter] = "Webcam"
					}
				},
				CancellationToken.None);

			Assert.That(result.Options.Select(o => o.Value), Is.EqualTo(_expectedFilterOptions));
		}
	}

	[Test]
	public async Task SetSourceFilter_SourceOptions_ListLiveSources()
	{
		var (connection, client) = ConnectedConnection();
		client.SourceNames = ["Gameplay", "Webcam", "Mic/Aux"];
		using (connection)
		{
			var action = new SetSourceFilterActionDefinition(() => connection);

			var result = await action.GetDynamicOptionsAsync(new DynamicOptionsContext
				{
					ParameterName = SetSourceFilterActionDefinition.SourceParameter,
					CurrentParameters = new Dictionary<string, object?>()
				},
				CancellationToken.None);

			Assert.That(result.Options.Select(o => o.Value), Is.EqualTo(_expectedSourceNameOptions));
		}
	}

	[Test]
	public async Task GetInputVolume_WritesRoundedPercentToVariable()
	{
		var (connection, client) = ConnectedConnection();
		client.InputVolumes["Mic/Aux"] = 0.5f;
		var variables = new RecordingVariableApi();
		var accessor = new VariableApiAccessor { Current = variables };
		using (connection)
		{
			var action = new GetInputVolumeActionDefinition(() => connection, accessor);

			await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
			{
				[GetInputVolumeActionDefinition.InputParameter] = "Mic/Aux",
				[GetInputVolumeActionDefinition.VariableParameter] = "mic_volume"
			}));
		}

		var handle = await variables.GetByNameAsync("mic_volume");
		Assert.Multiple(() =>
		{
			Assert.That(handle, Is.Not.Null);
			Assert.That(handle!.Value, Is.EqualTo(50d));
			Assert.That(handle.Type, Is.EqualTo(VariableType.Numeric));
		});
	}

	[Test]
	public async Task GetSourceFilterState_WritesEnabledStateToVariable()
	{
		var (connection, client) = ConnectedConnection();
		client.FilterStates["Webcam::Blur"] = true;
		var variables = new RecordingVariableApi();
		var accessor = new VariableApiAccessor { Current = variables };
		using (connection)
		{
			var action = new GetSourceFilterStateActionDefinition(() => connection, accessor);

			await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
			{
				[GetSourceFilterStateActionDefinition.SourceParameter] = "Webcam",
				[GetSourceFilterStateActionDefinition.FilterParameter] = "Blur",
				[GetSourceFilterStateActionDefinition.VariableParameter] = "blur_enabled"
			}));
		}

		var handle = await variables.GetByNameAsync("blur_enabled");
		Assert.Multiple(() =>
		{
			Assert.That(handle, Is.Not.Null);
			Assert.That(handle!.Value, Is.EqualTo(true));
			Assert.That(handle.Type, Is.EqualTo(VariableType.Boolean));
		});
	}

	private sealed class ThrowingVariableApi : IVariableApi
	{
		public Task<IReadOnlyList<VariableHandle>> GetAllAsync() => Task.FromResult<IReadOnlyList<VariableHandle>>([]);

		public Task<VariableHandle?> GetByNameAsync(string name) => Task.FromResult<VariableHandle?>(null);

		public Task<VariableHandle> CreateAsync(
			string name,
			VariableType type,
			object? initialValue = null,
			int? decimalPlaces = null,
			string? definitionId = null)
			=> throw new InvalidOperationException("variable store unavailable");

		public Task SetValueAsync(Guid variableId, object? value) => Task.CompletedTask;

		public Task DeleteAsync(Guid variableId) => Task.CompletedTask;
	}

	private static Dictionary<string, object?> SliderParameters(string? input, string mode)
	{
		var result = new Dictionary<string, object?> { [SetInputVolumeActionDefinition.ModeParameter] = mode };
		if (input is not null)
		{
			result[SetInputVolumeActionDefinition.InputParameter] = input;
		}

		return result;
	}

	private static Dictionary<string, JsonElement> SliderTargetParameters(string? input, string mode)
	{
		var result = new Dictionary<string, JsonElement>
		{
			[SetInputVolumeActionDefinition.ModeParameter] = JsonSerializer.SerializeToElement(mode)
		};
		if (input is not null)
		{
			result[SetInputVolumeActionDefinition.InputParameter] = JsonSerializer.SerializeToElement(input);
		}

		return result;
	}

	[Test]
	public void ObsActions_Catalog_ContainsAllExpectedActionIds()
	{
		var actions = ObsActions.Create(() => null, new VariableApiAccessor());

		Assert.That(actions.Select(a => a.Id),
			Is.SupersetOf(new[]
			{
				"get-input-volume",
				"get-source-filter-state",
				"get-input-mute",
				"get-source-visibility",
				"set-input-volume",
				"set-input-mute",
				"set-source-visibility",
				"set-source-filter"
			}));
	}

	[Test]
	public async Task GetInputMute_WritesMuteStateToVariable_AndReusesSameVariableAcrossRuns()
	{
		var variables = new RecordingVariableApi();
		var accessor = new VariableApiAccessor { Current = variables };

		var (mutedConnection, mutedClient) = ConnectedConnection();
		mutedClient.MutedInputs["Mic Aux"] = true;
		ActionResult mutedResult;
		using (mutedConnection)
		{
			var action = new GetInputMuteActionDefinition(() => mutedConnection, accessor);
			mutedResult = await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
			{
				[GetInputMuteActionDefinition.InputParameter] = "Mic Aux",
				[GetInputMuteActionDefinition.VariableParameter] = "obs_mic_muted"
			}));
		}

		var (unmutedConnection, unmutedClient) = ConnectedConnection();
		unmutedClient.MutedInputs["Mic Aux"] = false;
		ActionResult unmutedResult;
		using (unmutedConnection)
		{
			var action = new GetInputMuteActionDefinition(() => unmutedConnection, accessor);
			unmutedResult = await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
			{
				[GetInputMuteActionDefinition.InputParameter] = "Mic Aux",
				[GetInputMuteActionDefinition.VariableParameter] = "obs_mic_muted"
			}));
		}

		var handle = await variables.GetByNameAsync("obs_mic_muted");
		Assert.Multiple(() =>
		{
			Assert.That(mutedResult.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(unmutedResult.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(handle, Is.Not.Null);
			Assert.That(handle!.Type, Is.EqualTo(VariableType.Boolean));
			Assert.That(handle.Value, Is.EqualTo(false));
			Assert.That(variables.CreateCount, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task GetInputMute_LeavesVariableUnchanged_WhenReadFails()
	{
		var variables = new RecordingVariableApi();
		var accessor = new VariableApiAccessor { Current = variables };
		await variables.CreateAsync("obs_mic_muted", VariableType.Boolean, true);

		var notConnectedAction = new GetInputMuteActionDefinition(() => null, accessor);
		var notConnectedResult = await notConnectedAction.CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object>
			{
				[GetInputMuteActionDefinition.InputParameter] = "Mic Aux",
				[GetInputMuteActionDefinition.VariableParameter] = "obs_mic_muted"
			}));

		var (connection, client) = ConnectedConnection();
		client.DeletedNames.Add("Ghost Mic");
		ActionResult deletedResult;
		using (connection)
		{
			var deletedAction = new GetInputMuteActionDefinition(() => connection, accessor);
			deletedResult = await deletedAction.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
			{
				[GetInputMuteActionDefinition.InputParameter] = "Ghost Mic",
				[GetInputMuteActionDefinition.VariableParameter] = "obs_mic_muted"
			}));
		}

		var handle = await variables.GetByNameAsync("obs_mic_muted");
		Assert.Multiple(() =>
		{
			Assert.That(notConnectedResult.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(notConnectedResult.ErrorCode, Is.EqualTo(ActionErrorCodes.NotConnected));
			Assert.That(deletedResult.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(deletedResult.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
			Assert.That(TestLocalization.Resolve(deletedResult.ErrorMessage), Does.Contain("Ghost Mic"));
			Assert.That(handle, Is.Not.Null);
			Assert.That(handle!.Value, Is.EqualTo(true));
		});
	}

	[Test]
	public async Task GetInputMute_FailsWithInvalidParameter_AndCreatesNoVariable()
	{
		var (connection, client) = ConnectedConnection();
		client.MutedInputs["Mic Aux"] = true;
		var variables = new RecordingVariableApi();
		var accessor = new VariableApiAccessor { Current = variables };
		using (connection)
		{
			var action = new GetInputMuteActionDefinition(() => connection, accessor);

			var noInputResult = await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
			{
				[GetInputMuteActionDefinition.VariableParameter] = "obs_mic_muted"
			}));

			var blankVariableResult = await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
			{
				[GetInputMuteActionDefinition.InputParameter] = "Mic Aux",
				[GetInputMuteActionDefinition.VariableParameter] = "   "
			}));

			Assert.Multiple(() =>
			{
				Assert.That(noInputResult.Status, Is.EqualTo(ActionResultStatus.Failed));
				Assert.That(noInputResult.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
				Assert.That(blankVariableResult.Status, Is.EqualTo(ActionResultStatus.Failed));
				Assert.That(blankVariableResult.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
				Assert.That(variables.CreateCount, Is.EqualTo(0));
			});
		}
	}

	[Test]
	public async Task GetInputMute_NameCollision_NeverReportsSuccessWithoutWriting()
	{
		var (connection, client) = ConnectedConnection();
		client.MutedInputs["Mic Aux"] = true;
		var variables = new RecordingVariableApi();
		await variables.CreateAsync("collide", VariableType.Text, "hello");
		var accessor = new VariableApiAccessor { Current = variables };
		using (connection)
		{
			var action = new GetInputMuteActionDefinition(() => connection, accessor);

			var result = await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
			{
				[GetInputMuteActionDefinition.InputParameter] = "Mic Aux",
				[GetInputMuteActionDefinition.VariableParameter] = "collide"
			}));

			var handle = await variables.GetByNameAsync("collide");

			if (result.Status == ActionResultStatus.Succeeded)
			{
				Assert.That(handle!.Value, Is.EqualTo(true));
			}
			else
			{
				Assert.Multiple(() =>
				{
					Assert.That(result.ErrorCode, Is.Not.Null.And.Not.Empty);
					Assert.That(TestLocalization.Resolve(result.ErrorMessage), Is.Not.Null.And.Not.Empty);
					Assert.That(handle!.Value, Is.EqualTo("hello"));
				});
			}
		}
	}

	[Test]
	public async Task GetInputVolume_FailsWithProviderError_WhenVariableWriteFails()
	{
		var (connection, client) = ConnectedConnection();
		client.InputVolumes["Mic/Aux"] = 0.5f;
		var accessor = new VariableApiAccessor { Current = new ThrowingVariableApi() };
		using (connection)
		{
			var action = new GetInputVolumeActionDefinition(() => connection, accessor);

			var result = await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
			{
				[GetInputVolumeActionDefinition.InputParameter] = "Mic/Aux",
				[GetInputVolumeActionDefinition.VariableParameter] = "mic_volume"
			}));

			Assert.Multiple(() =>
			{
				Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
				Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderError));
			});
		}
	}

	[Test]
	public async Task GetInputMute_FailsWithProviderError_WhenVariableWriteFails()
	{
		var (connection, client) = ConnectedConnection();
		client.MutedInputs["Mic Aux"] = true;
		var accessor = new VariableApiAccessor { Current = new ThrowingVariableApi() };
		using (connection)
		{
			var action = new GetInputMuteActionDefinition(() => connection, accessor);

			var result = await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
			{
				[GetInputMuteActionDefinition.InputParameter] = "Mic Aux",
				[GetInputMuteActionDefinition.VariableParameter] = "obs_mic_muted"
			}));

			Assert.Multiple(() =>
			{
				Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
				Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderError));
			});
		}
	}

	[Test]
	public async Task GetSourceVisibility_SourceOptions_DependOnSelectedScene()
	{
		var (connection, client) = ConnectedConnection();
		client.SceneItems["Gameplay"] = ["Webcam", "Capture"];
		using (connection)
		{
			var action = new GetSourceVisibilityActionDefinition(() => connection, new VariableApiAccessor());

			var result = await action.GetDynamicOptionsAsync(new DynamicOptionsContext
				{
					ParameterName = GetSourceVisibilityActionDefinition.SourceParameter,
					CurrentParameters = new Dictionary<string, object?>
					{
						[GetSourceVisibilityActionDefinition.SceneParameter] = "Gameplay"
					}
				},
				CancellationToken.None);

			Assert.That(result.Options.Select(o => o.Value), Is.EqualTo(_expectedSourceOptions));
		}
	}

	[Test]
	public async Task GetSourceVisibility_WritesVisibilityPerScene_ForSameSourceInDifferentScenes()
	{
		var (connection, client) = ConnectedConnection();
		client.VisibleSources["Gameplay:Webcam"] = true;
		client.VisibleSources["Just Chatting:Webcam"] = false;
		var variables = new RecordingVariableApi();
		var accessor = new VariableApiAccessor { Current = variables };
		using (connection)
		{
			var action = new GetSourceVisibilityActionDefinition(() => connection, accessor);

			var chattingResult = await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
			{
				[GetSourceVisibilityActionDefinition.SceneParameter] = "Just Chatting",
				[GetSourceVisibilityActionDefinition.SourceParameter] = "Webcam",
				[GetSourceVisibilityActionDefinition.VariableParameter] = "cam_visible"
			}));

			var gameplayResult = await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
			{
				[GetSourceVisibilityActionDefinition.SceneParameter] = "Gameplay",
				[GetSourceVisibilityActionDefinition.SourceParameter] = "Webcam",
				[GetSourceVisibilityActionDefinition.VariableParameter] = "cam_visible_gameplay"
			}));

			var chattingHandle = await variables.GetByNameAsync("cam_visible");
			var gameplayHandle = await variables.GetByNameAsync("cam_visible_gameplay");

			Assert.Multiple(() =>
			{
				Assert.That(chattingResult.Status, Is.EqualTo(ActionResultStatus.Succeeded));
				Assert.That(gameplayResult.Status, Is.EqualTo(ActionResultStatus.Succeeded));
				Assert.That(chattingHandle, Is.Not.Null);
				Assert.That(chattingHandle!.Value, Is.EqualTo(false));
				Assert.That(gameplayHandle, Is.Not.Null);
				Assert.That(gameplayHandle!.Value, Is.EqualTo(true));
			});
		}
	}

	[Test]
	public async Task GetInputMute_IsNeverAnsweredFromTheOneSecondTargetReadCache()
	{
		// Same connection, same input, both reads inside the 1-second target-read cache's TTL: a flow
		// running "Set Input Mute" and then this getter in the same tick must not read back the pre-toggle
		// value the polled state provider's own cached read would still be serving.
		var (connection, client) = ConnectedConnection();
		client.MutedInputs["Mic Aux"] = true;
		var variables = new RecordingVariableApi();
		var accessor = new VariableApiAccessor { Current = variables };
		using (connection)
		{
			var action = new GetInputMuteActionDefinition(() => connection, accessor);

			var firstResult = await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
			{
				[GetInputMuteActionDefinition.InputParameter] = "Mic Aux",
				[GetInputMuteActionDefinition.VariableParameter] = "obs_mic_muted"
			}));

			// The OBS boundary changes state well within the cache's TTL.
			client.MutedInputs["Mic Aux"] = false;

			var secondResult = await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
			{
				[GetInputMuteActionDefinition.InputParameter] = "Mic Aux",
				[GetInputMuteActionDefinition.VariableParameter] = "obs_mic_muted"
			}));

			var handle = await variables.GetByNameAsync("obs_mic_muted");
			Assert.Multiple(() =>
			{
				Assert.That(firstResult.Status, Is.EqualTo(ActionResultStatus.Succeeded));
				Assert.That(secondResult.Status, Is.EqualTo(ActionResultStatus.Succeeded));
				Assert.That(handle!.Value,
					Is.EqualTo(false),
					"the explicit getter must report the current state, never one cached from a second ago");
			});
		}
	}

	[Test]
	public async Task GetSourceVisibility_IsNeverAnsweredFromTheOneSecondTargetReadCache()
	{
		var (connection, client) = ConnectedConnection();
		client.VisibleSources["Gameplay:Webcam"] = true;
		var variables = new RecordingVariableApi();
		var accessor = new VariableApiAccessor { Current = variables };
		using (connection)
		{
			var action = new GetSourceVisibilityActionDefinition(() => connection, accessor);

			var firstResult = await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
			{
				[GetSourceVisibilityActionDefinition.SceneParameter] = "Gameplay",
				[GetSourceVisibilityActionDefinition.SourceParameter] = "Webcam",
				[GetSourceVisibilityActionDefinition.VariableParameter] = "cam_visible"
			}));

			// The OBS boundary changes state well within the cache's TTL.
			client.VisibleSources["Gameplay:Webcam"] = false;

			var secondResult = await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
			{
				[GetSourceVisibilityActionDefinition.SceneParameter] = "Gameplay",
				[GetSourceVisibilityActionDefinition.SourceParameter] = "Webcam",
				[GetSourceVisibilityActionDefinition.VariableParameter] = "cam_visible"
			}));

			var handle = await variables.GetByNameAsync("cam_visible");
			Assert.Multiple(() =>
			{
				Assert.That(firstResult.Status, Is.EqualTo(ActionResultStatus.Succeeded));
				Assert.That(secondResult.Status, Is.EqualTo(ActionResultStatus.Succeeded));
				Assert.That(handle!.Value,
					Is.EqualTo(false),
					"the explicit getter must report the current state, never one cached from a second ago");
			});
		}
	}
}
