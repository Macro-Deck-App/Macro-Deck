using MacroDeckHost.Integrations.Voicemeeter;
using MacroDeckHost.Integrations.Voicemeeter.Actions;
using MacroDeckHost.Tests.UnitTests.System;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Tests.UnitTests.Voicemeeter;

[TestFixture]
internal sealed class VoicemeeterActionsTests
{
	private static readonly string[] _fadeWrite = ["Strip[0].FadeTo=(-10.0, 500)"];

	private static readonly string[] _exclusivePatch =
		["Strip[0].A1=0;Strip[0].A2=1;Strip[0].A3=0;Strip[0].B1=0;Strip[0].B2=0"];

	private static readonly string[] _pressWrites = ["MacroButton[4].PushRelease=1", "MacroButton[4].PushRelease=0"];
	private static readonly string[] _stateOnlyWrite = ["MacroButton[2].StateOnly=1"];
	private static readonly string[] _hideWrite = ["Command.Show=0"];
	private static readonly string[] _setParameterWrites = ["Strip[0].Comp=4.5", "Strip[0].device.wdm=Line In"];
	private static readonly string[] _scriptWrite = ["Strip[0].Mute=1\nBus[0].Gain=-6"];
	private static readonly string[] _loadWrite = [@"Command.Load=C:\mixes\stream.xml"];
	private static readonly string[] _bananaBusSends = ["A1", "A2", "A3", "B1", "B2"];

	private FakeVoicemeeterRemote _remote = null!;
	private VoicemeeterConnection _connection = null!;

	[SetUp]
	public void SetUp()
	{
		_remote = new FakeVoicemeeterRemote();
		_connection = new VoicemeeterConnection(_remote);
	}

	[TearDown]
	public void TearDown()
	{
		_connection.Dispose();
		_remote.Dispose();
	}

	[Test]
	public async Task Setting_a_strip_level_writes_the_gain_parameter()
	{
		Connected();

		await Run(Gain(VoicemeeterChannelTarget.Strip),
			new Dictionary<string, object>
			{
				["strip"] = "1",
				["mode"] = "set",
				["gain"] = -6.5d
			});

		Assert.That(_remote.Float("Strip[1].Gain"), Is.EqualTo(-6.5f).Within(0.001f));
	}

	[Test]
	public async Task Raising_a_level_twice_adds_up()
	{
		Connected();
		var parameters = new Dictionary<string, object>
		{
			["bus"] = "0",
			["mode"] = "increase",
			["gain"] = 3d
		};

		await Run(Gain(VoicemeeterChannelTarget.Bus), parameters);
		await Run(Gain(VoicemeeterChannelTarget.Bus), parameters);

		Assert.That(_remote.Float("Bus[0].Gain"), Is.EqualTo(6f).Within(0.001f));
	}

	[Test]
	public async Task A_level_can_never_leave_the_faders_range()
	{
		Connected();

		await Run(Gain(VoicemeeterChannelTarget.Strip),
			new Dictionary<string, object>
			{
				["strip"] = "0",
				["mode"] = "increase",
				["gain"] = 99d
			});

		Assert.That(_remote.Float("Strip[0].Gain"),
			Is.EqualTo((float)SetChannelGainActionDefinition.MaximumGain).Within(0.001f));
	}

	[Test]
	public async Task A_fade_is_handed_to_Voicemeeter_rather_than_stepped_from_here()
	{
		Connected();

		await Run(Gain(VoicemeeterChannelTarget.Strip),
			new Dictionary<string, object>
			{
				["strip"] = "0",
				["mode"] = "set",
				["gain"] = -10d,
				["fade"] = 500d
			});

		Assert.That(_remote.Writes, Is.EqualTo(_fadeWrite));
	}

	[Test]
	public async Task Toggling_mute_inverts_what_Voicemeeter_currently_has()
	{
		Connected();
		_remote.UserSets("Strip[2].Mute", 1f);

		await Run(new SetChannelMuteActionDefinition(VoicemeeterChannelTarget.Strip, () => _connection),
			new Dictionary<string, object> { ["strip"] = "2", ["mode"] = "toggle" });

		Assert.That(_remote.Float("Strip[2].Mute"), Is.Zero);
	}

	[Test]
	public async Task A_switch_action_targets_the_named_switch()
	{
		Connected();

		await Run(new SetChannelSwitchActionDefinition(VoicemeeterChannelTarget.Bus, () => _connection),
			new Dictionary<string, object>
			{
				["bus"] = "1",
				["switch"] = VoicemeeterParameters.Eq,
				["mode"] = "on"
			});

		Assert.That(_remote.Float("Bus[1].EQ.on"), Is.EqualTo(1f));
	}

	[Test]
	public async Task A_switch_action_without_a_switch_selected_fails_with_invalid_parameter()
	{
		Connected();

		var result = await Run(new SetChannelSwitchActionDefinition(VoicemeeterChannelTarget.Bus, () => _connection),
			new Dictionary<string, object> { ["bus"] = "1", ["mode"] = "on" });

		Assert.Multiple(() =>
		{
			Assert.That(_remote.Writes, Is.Empty);
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
		});
	}

	[Test]
	public async Task Routing_a_strip_to_a_bus_writes_that_send()
	{
		Connected();

		await Run(new SetStripRoutingActionDefinition(() => _connection),
			new Dictionary<string, object>
			{
				["strip"] = "0",
				["bus"] = "B1",
				["mode"] = "on"
			});

		Assert.That(_remote.Float("Strip[0].B1"), Is.EqualTo(1f));
	}

	[Test]
	public async Task Routing_without_a_bus_selected_fails_with_invalid_parameter()
	{
		Connected();

		var result = await Run(new SetStripRoutingActionDefinition(() => _connection),
			new Dictionary<string, object> { ["strip"] = "0", ["mode"] = "on" });

		Assert.Multiple(() =>
		{
			Assert.That(_remote.Writes, Is.Empty);
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
		});
	}

	[Test]
	public async Task An_exclusive_patch_is_sent_as_a_single_script()
	{
		Connected();
		_remote.UserSets("Strip[0].A1", 1f);
		_remote.UserSets("Strip[0].A3", 1f);
		_connection.Poll();
		_remote.Writes.Clear();

		await Run(new SetStripRoutingActionDefinition(() => _connection),
			new Dictionary<string, object>
			{
				["strip"] = "0",
				["bus"] = "A2",
				["mode"] = "only"
			});

		Assert.Multiple(() =>
		{
			Assert.That(_remote.Writes,
				Is.EqualTo(_exclusivePatch));
			Assert.That(_remote.Float("Strip[0].A2"), Is.EqualTo(1f));
			Assert.That(_remote.Float("Strip[0].A1"), Is.Zero);
			Assert.That(_remote.Float("Strip[0].A3"), Is.Zero);
		});
	}

	[Test]
	public async Task Pressing_a_macro_button_releases_it_again()
	{
		Connected();

		await Run(new SetMacroButtonActionDefinition(() => _connection),
			new Dictionary<string, object> { ["button"] = 4d, ["mode"] = "press" });

		Assert.That(_remote.Writes,
			Is.EqualTo(_pressWrites));
	}

	[Test]
	public async Task A_macro_button_can_change_its_light_without_running_what_it_does()
	{
		Connected();

		await Run(new SetMacroButtonActionDefinition(() => _connection),
			new Dictionary<string, object>
			{
				["button"] = 2d,
				["mode"] = "on",
				["stateOnly"] = true
			});

		Assert.That(_remote.Writes, Is.EqualTo(_stateOnlyWrite));
	}

	[Test]
	public async Task An_out_of_range_macro_button_is_refused()
	{
		Connected();

		var result = await Run(new SetMacroButtonActionDefinition(() => _connection),
			new Dictionary<string, object> { ["button"] = 80d, ["mode"] = "press" });

		Assert.Multiple(() =>
		{
			Assert.That(_remote.Writes, Is.Empty);
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
		});
	}

	[Test]
	public async Task Toggling_a_macro_button_while_Voicemeeter_is_closed_fails_with_a_provider_error()
	{
		var result = await Run(new SetMacroButtonActionDefinition(() => _connection),
			new Dictionary<string, object> { ["button"] = 4d, ["mode"] = "toggle" });

		Assert.Multiple(() =>
		{
			Assert.That(_remote.Writes, Is.Empty);
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderError));
		});
	}

	[Test]
	public async Task A_macro_button_action_without_a_connection_fails_with_not_connected()
	{
		var result = await Run(new SetMacroButtonActionDefinition(() => null),
			new Dictionary<string, object> { ["button"] = 4d, ["mode"] = "press" });

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotConnected));
	}

	[Test]
	public async Task A_command_writes_the_parameter_it_maps_to()
	{
		Connected();
		var action = VoicemeeterActions.Create(() => _connection, new VoicemeeterVariableAccessor())
			.Single(candidate => candidate.Id == "run-command");

		await Run(action, new Dictionary<string, object> { ["command"] = "hide" });

		Assert.That(_remote.Writes, Is.EqualTo(_hideWrite));
	}

	[Test]
	public async Task An_unknown_command_writes_nothing()
	{
		Connected();
		var action = VoicemeeterActions.Create(() => _connection, new VoicemeeterVariableAccessor())
			.Single(candidate => candidate.Id == "control-recorder");

		var result = await Run(action, new Dictionary<string, object> { ["command"] = "teleport" });

		Assert.Multiple(() =>
		{
			Assert.That(_remote.Writes, Is.Empty);
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
		});
	}

	[Test]
	public async Task A_command_action_without_a_connection_fails_with_not_connected()
	{
		var action = VoicemeeterActions.Create(() => null, new VoicemeeterVariableAccessor())
			.Single(candidate => candidate.Id == "run-command");

		var result = await Run(action, new Dictionary<string, object> { ["command"] = "hide" });

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotConnected));
	}

	[Test]
	public async Task Set_parameter_writes_text_as_text_and_numbers_as_numbers()
	{
		Connected();
		var action = new SetParameterActionDefinition(() => _connection);

		await Run(action, new Dictionary<string, object> { ["parameter"] = "Strip[0].Comp", ["value"] = "4.5" });
		await Run(action,
			new Dictionary<string, object> { ["parameter"] = "Strip[0].device.wdm", ["value"] = "Line In" });

		Assert.That(_remote.Writes, Is.EqualTo(_setParameterWrites));
	}

	[Test]
	public async Task Setting_a_parameter_without_a_connection_fails_with_not_connected()
	{
		var action = new SetParameterActionDefinition(() => null);

		var result = await Run(action, new Dictionary<string, object> { ["parameter"] = "Strip[0].Comp" });

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotConnected));
	}

	[Test]
	public async Task Setting_a_parameter_without_a_name_fails_with_invalid_parameter()
	{
		Connected();
		var action = new SetParameterActionDefinition(() => _connection);

		var result = await Run(action, new Dictionary<string, object> { ["value"] = "4.5" });

		Assert.Multiple(() =>
		{
			Assert.That(_remote.Writes, Is.Empty);
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
		});
	}

	[Test]
	public async Task A_script_is_handed_over_unchanged()
	{
		Connected();

		await Run(new RunScriptActionDefinition(() => _connection),
			new Dictionary<string, object> { ["script"] = "Strip[0].Mute=1\nBus[0].Gain=-6" });

		Assert.Multiple(() =>
		{
			Assert.That(_remote.Writes, Is.EqualTo(_scriptWrite));
			Assert.That(_remote.Float("Strip[0].Mute"), Is.EqualTo(1f));
			Assert.That(_remote.Float("Bus[0].Gain"), Is.EqualTo(-6f));
		});
	}

	[Test]
	public async Task Reading_a_parameter_writes_it_into_a_variable()
	{
		Connected();
		_remote.UserSets("Strip[0].Comp", 3.25f);
		var variables = new RecordingVariableApi();
		var accessor = new VoicemeeterVariableAccessor { Current = variables };

		await Run(new GetParameterActionDefinition(() => _connection, accessor),
			new Dictionary<string, object>
			{
				["parameter"] = "Strip[0].Comp",
				["variable"] = "comp_amount",
				["type"] = "number"
			});

		var handle = await variables.GetByNameAsync("comp_amount");
		Assert.Multiple(() =>
		{
			Assert.That(handle, Is.Not.Null);
			Assert.That(handle!.Value, Is.EqualTo(3.25d));
		});
	}

	[Test]
	public async Task Reading_an_unknown_parameter_writes_no_variable()
	{
		Connected();
		var variables = new RecordingVariableApi();

		var result = await Run(new GetParameterActionDefinition(() => _connection,
				new VoicemeeterVariableAccessor
				{
					Current = variables
				}),
			new Dictionary<string, object>
			{
				["parameter"] = "Strip[99].Nonsense",
				["variable"] = "nope",
				["type"] = "number"
			});

		Assert.Multiple(() =>
		{
			Assert.That(variables.CreateCount, Is.Zero);
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderError));
		});
	}

	[Test]
	public async Task Reading_a_parameter_without_a_connection_fails_with_not_connected()
	{
		var variables = new RecordingVariableApi();

		var result = await Run(
			new GetParameterActionDefinition(() => null, new VoicemeeterVariableAccessor { Current = variables }),
			new Dictionary<string, object> { ["parameter"] = "Strip[0].Comp", ["variable"] = "comp_amount" });

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotConnected));
	}

	[Test]
	public async Task Reading_a_parameter_without_a_target_variable_fails_with_invalid_parameter()
	{
		Connected();
		var variables = new RecordingVariableApi();

		var result = await Run(new GetParameterActionDefinition(() => _connection,
				new VoicemeeterVariableAccessor { Current = variables }),
			new Dictionary<string, object> { ["parameter"] = "Strip[0].Comp" });

		Assert.Multiple(() =>
		{
			Assert.That(variables.CreateCount, Is.Zero);
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
		});
	}

	[Test]
	public async Task Nothing_is_written_when_no_channel_was_picked()
	{
		Connected();

		var result = await Run(Gain(VoicemeeterChannelTarget.Strip),
			new Dictionary<string, object> { ["mode"] = "set", ["gain"] = -6d });

		Assert.Multiple(() =>
		{
			Assert.That(_remote.Writes, Is.Empty);
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
		});
	}

	[Test]
	public async Task A_gain_action_without_a_connection_fails_with_not_connected()
	{
		var result = await Run(new SetChannelGainActionDefinition(VoicemeeterChannelTarget.Strip, () => null),
			new Dictionary<string, object> { ["strip"] = "0", ["mode"] = "set", ["gain"] = -6d });

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotConnected));
	}

	[Test]
	public async Task Raising_a_level_the_provider_cannot_read_fails_with_a_provider_error()
	{
		Connected();

		var result = await Run(Gain(VoicemeeterChannelTarget.Strip),
			new Dictionary<string, object> { ["strip"] = "999", ["mode"] = "increase", ["gain"] = 3d });

		Assert.Multiple(() =>
		{
			Assert.That(_remote.Writes, Is.Empty);
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderError));
		});
	}

	[Test]
	public async Task The_channel_picker_offers_the_users_own_labels()
	{
		Connected();
		_remote.UserSets("Strip[0].Label", "Mic");
		_connection.Poll();

		var options = await Gain(VoicemeeterChannelTarget.Strip).GetDynamicOptionsAsync(new DynamicOptionsContext
			{
				ParameterName = "strip",
				CurrentParameters = new Dictionary<string, object?>()
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(TestLocalization.Resolve(options.Options[0].Label), Is.EqualTo("Mic"));
			Assert.That(options.Options[0].Value, Is.EqualTo("0"));
			Assert.That(TestLocalization.Resolve(options.Options[1].Label), Is.EqualTo("Strip 2"));
			Assert.That(options.Options, Has.Count.EqualTo(5));
		});
	}

	[Test]
	public async Task The_channel_picker_still_answers_before_Voicemeeter_has_ever_run()
	{
		var options = await Gain(VoicemeeterChannelTarget.Bus).GetDynamicOptionsAsync(new DynamicOptionsContext
			{
				ParameterName = "bus",
				CurrentParameters = new Dictionary<string, object?>()
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(options.Options, Has.Count.EqualTo(8));
			Assert.That(TestLocalization.Resolve(options.Options[0].Label), Is.EqualTo("Bus A1"));
			Assert.That(TestLocalization.Resolve(options.Options[5].Label), Is.EqualTo("Bus B1"));
			Assert.That(options.AllowsCustomValue, Is.True);
		});
	}

	[Test]
	public async Task The_routing_picker_offers_bus_sends_rather_than_bus_indexes()
	{
		Connected();

		var options = await new SetStripRoutingActionDefinition(() => _connection).GetDynamicOptionsAsync(
			new DynamicOptionsContext
			{
				ParameterName = "bus",
				CurrentParameters = new Dictionary<string, object?>()
			},
			CancellationToken.None);

		Assert.That(options.Options.Select(option => option.Value),
			Is.EqualTo(_bananaBusSends));
	}

	[Test]
	public async Task Run_Voicemeeter_starts_the_edition_that_is_installed()
	{
		_remote.InstalledEditions.Add(VoicemeeterEdition.Banana);

		await Run(new RunVoicemeeterActionDefinition(() => _connection),
			new Dictionary<string, object> { ["edition"] = RunVoicemeeterActionDefinition.AutoEdition });

		Assert.That(_remote.Edition, Is.EqualTo(VoicemeeterEdition.Banana));
	}

	[Test]
	public async Task Run_Voicemeeter_does_nothing_when_it_is_already_running()
	{
		Connected();
		_remote.InstalledEditions.Add(VoicemeeterEdition.Banana);

		await Run(new RunVoicemeeterActionDefinition(() => _connection),
			new Dictionary<string, object> { ["edition"] = RunVoicemeeterActionDefinition.AutoEdition });

		Assert.That(_remote.RunRequests, Is.Empty);
	}

	[Test]
	public async Task Run_Voicemeeter_without_the_remote_library_fails_with_not_connected()
	{
		var result = await Run(new RunVoicemeeterActionDefinition(() => null),
			new Dictionary<string, object> { ["edition"] = RunVoicemeeterActionDefinition.AutoEdition });

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotConnected));
	}

	[Test]
	public async Task Run_Voicemeeter_fails_with_a_provider_error_when_no_edition_can_be_started()
	{
		var result = await Run(new RunVoicemeeterActionDefinition(() => _connection),
			new Dictionary<string, object> { ["edition"] = RunVoicemeeterActionDefinition.AutoEdition });

		Assert.Multiple(() =>
		{
			Assert.That(_remote.Edition, Is.EqualTo(VoicemeeterEdition.None));
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderError));
		});
	}

	[Test]
	public async Task Loading_settings_hands_the_file_to_Voicemeeter()
	{
		Connected();

		await Run(new LoadSettingsActionDefinition(() => _connection),
			new Dictionary<string, object> { ["file"] = @"C:\mixes\stream.xml" });

		Assert.That(_remote.Writes, Is.EqualTo(_loadWrite));
	}

	[Test]
	public async Task Loading_settings_without_a_file_fails_with_invalid_parameter()
	{
		Connected();

		var result = await Run(new LoadSettingsActionDefinition(() => _connection),
			new Dictionary<string, object>());

		Assert.Multiple(() =>
		{
			Assert.That(_remote.Writes, Is.Empty);
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
		});
	}

	private SetChannelGainActionDefinition Gain(VoicemeeterChannelTarget target)
		=> new(target, () => _connection);

	[Test]
	public async Task A_strip_mute_button_reports_the_strips_real_mute_state()
	{
		Connected();
		_remote.UserSets(VoicemeeterParameters.Strip(0, VoicemeeterParameters.Mute), 1f);
		_connection.Poll();
		var provider = Provider("set-strip-mute");

		var snapshot = await provider.GetActionStateAsync(
			new Dictionary<string, object?> { [VoicemeeterActionValues.StripParameter] = 0 },
			CancellationToken.None);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("muted"));
	}

	[Test]
	public async Task A_mute_button_with_no_strip_chosen_yet_reports_no_state_at_all()
	{
		Connected();
		var provider = Provider("set-strip-mute");

		var snapshot = await provider.GetActionStateAsync(new Dictionary<string, object?>(), CancellationToken.None);

		Assert.That(snapshot, Is.Null);
	}

	[Test]
	public async Task A_mute_button_is_unavailable_while_voicemeeter_is_not_running()
	{
		var provider = Provider("set-strip-mute", running: false);

		var snapshot = await provider.GetActionStateAsync(
			new Dictionary<string, object?> { [VoicemeeterActionValues.StripParameter] = 0 },
			CancellationToken.None);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("unavailable"));
	}

	[Test]
	public async Task A_switch_button_reports_mono_but_admits_it_cannot_read_eq()
	{
		Connected();
		_remote.UserSets(VoicemeeterParameters.Bus(0, VoicemeeterParameters.Mono), 1f);
		_connection.Poll();
		var provider = Provider("set-bus-switch");

		var mono = await provider.GetActionStateAsync(new Dictionary<string, object?>
			{
				[VoicemeeterActionValues.BusParameter] = 0,
				[VoicemeeterActionValues.SwitchParameter] = VoicemeeterParameters.Mono
			},
			CancellationToken.None);
		var eq = await provider.GetActionStateAsync(new Dictionary<string, object?>
			{
				[VoicemeeterActionValues.BusParameter] = 0,
				[VoicemeeterActionValues.SwitchParameter] = VoicemeeterParameters.Eq
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(mono!.ActiveStateId, Is.EqualTo("on"));
			Assert.That(eq!.ActiveStateId, Is.EqualTo("unavailable"));
		});
	}

	[Test]
	public async Task A_macro_button_reports_whether_that_button_is_on()
	{
		Connected();
		_remote.UserPressesMacroButton(4, true);
		_connection.Poll();
		var provider = Provider("set-macro-button");

		var snapshot = await provider.GetActionStateAsync(
			new Dictionary<string, object?> { [SetMacroButtonActionDefinition.ButtonParameter] = 4 },
			CancellationToken.None);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("on"));
	}

	private IStateProviderActionDefinition Provider(string actionId, bool running = true)
		=> (IStateProviderActionDefinition)VoicemeeterActions
			.Create(() => running ? _connection : null, new VoicemeeterVariableAccessor())
			.Single(action => action.Id == actionId);

	private void Connected()
	{
		_remote.Run(VoicemeeterEdition.Banana);
		_connection.Poll();
		_remote.Writes.Clear();
	}

	private static Task<ActionResult> Run(IActionDefinition action, Dictionary<string, object> parameters)
		=> action.CreateExecutor().ExecuteAsync(new ActionExecutionContext { Parameters = parameters });
}
