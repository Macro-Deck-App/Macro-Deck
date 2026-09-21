using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Testing;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Actions;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Tests.UnitTests.Delegation;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Widgets.ActionButton;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

[TestFixture]
public class ActionButtonProviderConfigTests
{
	private const string IntegrationId = "test.providers";

	private ControllableStateProviderAction _mute = null!;
	private ControllableIconProviderAction _cover = null!;
	private FakeIntegrationRegistry _registry = null!;
	private FakeTimeProvider _time = null!;

	[SetUp]
	public void SetUp()
	{
		_mute = new ControllableStateProviderAction
		{
			Answer = (_, _) => Task.FromResult<ActionStateSnapshot?>(ControllableStateProviderAction.Snapshot(
				new ActionStateDefinition("unmuted", MacroDeckStrings.States.Unmuted()),
				new ActionStateDefinition("muted", MacroDeckStrings.States.Muted())
				{
					DefaultAppearance = new ActionStateAppearance { BackgroundColor = "#c53030" },
				})),
		};
		_cover = new ControllableIconProviderAction();
		_registry = new FakeIntegrationRegistry();
		_registry.Add(new FakeIntegration
		{
			Id = IntegrationId, Actions = [_mute, _cover, new StateAndIconProviderAction(), new CapturingActionDefinition()],
		});
		_registry.Add(RemoteIconProviderFixture.Integration());
		_time = new FakeTimeProvider();
	}

	[Test]
	public void The_action_list_offers_state_and_icon_adoption_and_names_no_provider_yet()
	{
		var flows = Render(new { flows = Flow(Block("b1", "mute")) }).ById("flows");

		Assert.Multiple(() =>
		{
			Assert.That(flows.Flag(UiConfigProperties.OffersStateProvider), Is.True);
			Assert.That(flows.Flag(UiConfigProperties.OffersIconProvider), Is.True);
			Assert.That(flows.Text(UiConfigProperties.StateProviderBlockId), Is.Empty);
			Assert.That(Events(flows), Does.Contain(UiConfigEvents.Provide));
		});
	}

	[Test]
	public async Task Adopting_the_second_capable_block_makes_it_the_saved_state_provider_with_readable_states()
	{
		var host = Render(new
		{
			stateMode = false,
			backgroundColor = "#101010",
			flows = Flow(Block("b1", "mute"), Block("b2", "mute")),
		});

		host.ById("flows").Raise(UiConfigEvents.Provide, new { capability = "state", blockId = "b2", enabled = true });
		await host.SettleAsync();

		var provider = host.ById("stateProvider").Property(UiConfigProperties.Value)!.Value;
		var states = host.ById("states").Property(UiConfigProperties.Value)!.Value.EnumerateArray().ToList();

		Assert.Multiple(() =>
		{
			Assert.That(host.ById("stateMode").Flag(UiConfigProperties.Value), Is.True);
			Assert.That(provider.GetProperty("blockId").GetString(), Is.EqualTo("b2"));
			Assert.That(provider.GetProperty("states").EnumerateArray().Select(s => s.GetProperty("label").GetString()),
				Is.EqualTo(new[] { "Unmuted", "Muted" }).AsCollection);
			Assert.That(states.Select(s => s.GetProperty("label").GetString()),
				Is.EqualTo(new[] { "Unmuted", "Muted" }).AsCollection);
			Assert.That(states[1].GetProperty("appearance").GetProperty("backgroundColor").GetString(),
				Is.EqualTo("#c53030"));
			Assert.That(host.ById("manualStateBackup").Property(UiConfigProperties.Value)!.Value.ValueKind,
				Is.EqualTo(JsonValueKind.Object));
			Assert.That(host.ById("activeStateId").Text(UiConfigProperties.Value), Is.EqualTo("unmuted"));
			Assert.That(host.ById("flows").Text(UiConfigProperties.StateProviderBlockId), Is.EqualTo("b2"));
		});
	}

	[Test]
	public async Task Stopping_a_state_provider_restores_the_manual_states_and_saves_no_provider()
	{
		var host = Render(new
		{
			stateMode = true,
			states = new object[] { new { id = "off", label = "Off" }, new { id = "on", label = "On" } },
			activeStateId = "on",
			flows = Flow(Block("b1", "mute")),
		});

		host.ById("flows").Raise(UiConfigEvents.Provide, new { capability = "state", blockId = "b1", enabled = true });
		await host.SettleAsync();
		host.ById("flows").Raise(UiConfigEvents.Provide, new { capability = "state", blockId = "b1", enabled = false });
		await host.SettleAsync();

		Assert.That(ProviderBlockId(host), Is.EqualTo("b1"), "turning the card control off asks first");

		host.ById("root.properties.stopStateProvider.stopStateProviderConfirm").Activate();

		Assert.Multiple(() =>
		{
			Assert.That(StateIds(host), Is.EqualTo(new[] { "off", "on" }).AsCollection);
			Assert.That(host.ById("activeStateId").Text(UiConfigProperties.Value), Is.EqualTo("on"));
			Assert.That(host.ById("stateProvider").Property(UiConfigProperties.Value)!.Value.ValueKind,
				Is.EqualTo(JsonValueKind.Null));
			Assert.That(host.ById("manualStateBackup").Property(UiConfigProperties.Value)!.Value.ValueKind,
				Is.EqualTo(JsonValueKind.Null));
		});
	}

	[Test]
	public async Task A_single_state_button_keeps_its_dormant_states_through_adopting_and_stopping_a_provider()
	{
		var host = Render(new
		{
			stateMode = false,
			states = new object[]
			{
				new { id = "a", label = "A" }, new { id = "b", label = "B" }, new { id = "c", label = "C" },
			},
			flows = Flow(Block("b1", "mute")),
		});

		host.ById("flows").Raise(UiConfigEvents.Provide, new { capability = "state", blockId = "b1", enabled = true });
		await host.SettleAsync();
		host.ById("root.properties.stateProviderStatus.removeStateProvider").Activate();

		Assert.Multiple(() =>
		{
			Assert.That(StateIds(host), Is.EqualTo(new[] { "a", "b", "c" }).AsCollection);
			Assert.That(host.ById("stateProvider").Property(UiConfigProperties.Value)!.Value.ValueKind,
				Is.EqualTo(JsonValueKind.Null));
		});
	}

	[Test]
	public async Task The_live_state_line_never_names_a_state_the_adopted_provider_does_not_have()
	{
		var host = Render(new
		{
			stateMode = true,
			states = new object[] { new { id = "a", label = "A" } },
			flows = Flow(Block("b1", "mute")),
		},
		new WidgetStateOption("a", "A"));

		host.ById("flows").Raise(UiConfigEvents.Provide, new { capability = "state", blockId = "b1", enabled = true });
		await host.SettleAsync();

		Assert.That(host.FindById("root.properties.live-state"), Is.Null);
	}

	[Test]
	public async Task Switching_back_to_single_state_stops_the_provider_and_restores_the_manual_states()
	{
		var host = Render(new
		{
			stateMode = true,
			states = new object[] { new { id = "off", label = "Off" }, new { id = "on", label = "On" } },
			flows = Flow(Block("b1", "mute")),
		});
		host.ById("flows").Raise(UiConfigEvents.Provide, new { capability = "state", blockId = "b1", enabled = true });
		await host.SettleAsync();

		host.ById("stateMode").Change(false);

		Assert.Multiple(() =>
		{
			Assert.That(host.ById("stateMode").Flag(UiConfigProperties.Value), Is.False);
			Assert.That(ProviderBlockId(host), Is.Null);
			Assert.That(host.ById("flows").Text(UiConfigProperties.StateProviderBlockId), Is.Empty);
		});
	}

	[Test]
	public async Task Adding_an_icon_capable_action_that_has_no_icon_to_give_offers_nothing()
	{
		_cover.Icon = null;
		var host = Render(new { flows = Flow() });

		host.ById("flows").Change(Flow(Block("b1", "cover")));
		await SettleAfterDelayAsync(host);

		Assert.That(host.ByType("button").Select(button => button.Id), Has.None.Contains("acceptIconProvider"));
	}

	[Test]
	public async Task Stopping_a_provider_asks_first()
	{
		var host = Render(new { flows = Flow(Block("b1", "mute"), Block("b2", "cover")) });
		host.ById("flows").Raise(UiConfigEvents.Provide, new { capability = "state", blockId = "b1", enabled = true });
		host.ById("flows").Raise(UiConfigEvents.Provide, new { capability = "icon", blockId = "b2", enabled = true });
		await host.SettleAsync();

		var stopState = host.ById("root.properties.stateProviderStatus.removeStateProvider");
		var stopIcon = host.ByType("button").Single(button => button.Id.EndsWith("removeIconProvider-unmuted", StringComparison.Ordinal));

		Assert.Multiple(() =>
		{
			Assert.That(stopState.HasProperty(UiConfigProperties.ConfirmMessage), Is.True);
			Assert.That(stopState.Flag(UiConfigProperties.ConfirmDanger), Is.True);
			Assert.That(stopIcon.HasProperty(UiConfigProperties.ConfirmMessage), Is.True);
		});
	}

	[Test]
	public async Task Editing_the_icon_providers_settings_asks_it_for_an_icon_once_they_settle()
	{
		var host = Render(new { flows = Flow(Block("b1", "cover")) });
		host.ById("flows").Raise(UiConfigEvents.Provide, new { capability = "icon", blockId = "b1", enabled = true });
		await host.SettleAsync();
		var callsAfterAdoption = _cover.Calls;

		host.ById("flows").Change(Flow(Block("b1", "cover", device: "a")));
		host.ById("flows").Change(Flow(Block("b1", "cover", device: "ab")));
		host.ById("flows").Change(Flow(Block("b1", "cover", device: "abc")));

		Assert.That(_cover.Calls, Is.EqualTo(callsAfterAdoption), "nothing is asked while the edit is still going on");

		await SettleAfterDelayAsync(host);

		Assert.That(_cover.Calls - callsAfterAdoption, Is.EqualTo(1));
	}

	[Test]
	public void A_button_whose_flows_repeat_a_block_id_still_opens()
	{
		var host = Render(new { flows = Flow(Block("b1", "mute"), Block("b1", "cover")) });

		Assert.That(host.FindById("flows"), Is.Not.Null);
	}

	[Test]
	public async Task Switching_to_another_state_provider_asks_first_and_keeps_the_original_manual_states()
	{
		var host = Render(new
		{
			stateMode = true,
			states = new object[] { new { id = "off", label = "Off" } },
			flows = Flow(Block("b1", "mute", label: "First"), Block("b2", "mute", label: "Second")),
		});

		host.ById("flows").Raise(UiConfigEvents.Provide, new { capability = "state", blockId = "b1", enabled = true });
		await host.SettleAsync();
		host.ById("flows").Raise(UiConfigEvents.Provide, new { capability = "state", blockId = "b2", enabled = true });
		await host.SettleAsync();

		Assert.That(ProviderBlockId(host), Is.EqualTo("b1"), "nothing switches before the user confirms");

		host.ById("root.properties.providerSwitch.providerSwitchConfirm").Activate();

		var backup = host.ById("manualStateBackup").Property(UiConfigProperties.Value)!.Value;

		Assert.Multiple(() =>
		{
			Assert.That(ProviderBlockId(host), Is.EqualTo("b2"));
			Assert.That(host.ById("root.properties.stateProviderStatus").Text(UiConfigProperties.Value),
				Is.EqualTo("Second"));
			Assert.That(backup.GetProperty("states").EnumerateArray().Select(s => s.GetProperty("id").GetString()),
				Is.EqualTo(new[] { "off" }).AsCollection);
		});
	}

	[Test]
	public async Task A_block_whose_action_reports_no_states_is_not_adopted_and_says_why()
	{
		_mute.Answer = (_, _) => Task.FromResult<ActionStateSnapshot?>(null);
		var host = Render(new { flows = Flow(Block("b1", "mute")) });

		host.ById("flows").Raise(UiConfigEvents.Provide, new { capability = "state", blockId = "b1", enabled = true });
		await host.SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(host.ById("stateProvider").Property(UiConfigProperties.Value)!.Value.ValueKind,
				Is.EqualTo(JsonValueKind.Null));
			Assert.That(host.ToCanonicalJson(), Does.Contain("Widgets.Editor.ProviderReturnedNoStates"));
		});
	}

	[Test]
	public async Task A_disabled_block_is_not_adopted()
	{
		var host = Render(new { flows = Flow(Block("b1", "mute", disabled: true)) });

		host.ById("flows").Raise(UiConfigEvents.Provide, new { capability = "state", blockId = "b1", enabled = true });
		await host.SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_mute.Calls, Is.Zero);
			Assert.That(ProviderBlockId(host), Is.Null);
		});
	}

	[Test]
	public async Task A_plugin_action_that_provides_an_icon_can_be_adopted_as_the_icon_provider()
	{
		var host = Render(new
		{
			flows = Flow(new
			{
				id = "b1",
				integrationId = RemoteIconProviderFixture.PluginId,
				actionId = RemoteIconProviderFixture.ActionId,
				label = "Artwork",
				parameters = Array.Empty<object>(),
			}),
		});

		host.ById("flows").Raise(UiConfigEvents.Provide, new { capability = "icon", blockId = "b1", enabled = true });
		await host.SettleAsync();

		var provider = host.ById("iconProvider").Property(UiConfigProperties.Value)!.Value;

		Assert.Multiple(() =>
		{
			Assert.That(provider.GetProperty("blockId").GetString(), Is.EqualTo("b1"));
			Assert.That(host.ById("flows").Text(UiConfigProperties.IconProviderBlockId), Is.EqualTo("b1"));
			Assert.That(host.ById("icon").Flag(UiConfigProperties.Disabled), Is.True);
		});
	}

	[Test]
	public async Task Adding_a_state_capable_action_offers_it_and_accepting_on_a_single_state_button_turns_multi_state_on()
	{
		var host = Render(new { stateMode = false, flows = Flow(Block("existing", "mute")) });

		host.ById("flows").Change(Flow(Block("existing", "mute"), Block("added", "mute")));
		await SettleAfterDelayAsync(host);

		Assert.That(host.FindById("root.properties.stateProviderOffer.acceptStateProvider"), Is.Not.Null);

		host.ById("root.properties.stateProviderOffer.acceptStateProvider").Activate();

		Assert.Multiple(() =>
		{
			Assert.That(host.ById("stateMode").Flag(UiConfigProperties.Value), Is.True);
			Assert.That(ProviderBlockId(host), Is.EqualTo("added"));
		});
	}

	[Test]
	public async Task Neither_an_action_present_at_open_nor_one_that_provides_nothing_produces_an_offer()
	{
		var host = Render(new { flows = Flow(Block("existing", "mute")) });

		host.ById("flows").Change(Flow(Block("existing", "mute"), Block("plain", "capture")));
		await SettleAfterDelayAsync(host);

		Assert.Multiple(() =>
		{
			Assert.That(host.FindById("root.properties.stateProviderOffer.acceptStateProvider"), Is.Null);
			Assert.That(host.FindById("root.properties.combinedProviderOffer.acceptCombinedProvider"), Is.Null);
		});
	}

	[Test]
	public async Task Editing_a_just_added_actions_settings_before_its_offer_appears_offers_what_the_edited_action_reports()
	{
		_mute.Answer = (parameters, _) => Task.FromResult<ActionStateSnapshot?>(
			ControllableStateProviderAction.Snapshot(
				new ActionStateDefinition((parameters.GetValueOrDefault("device") as string) ?? "none", "Device")));
		var host = Render(new { flows = Flow() });

		host.ById("flows").Change(Flow(Block("b1", "mute", device: "speakers")));
		host.ById("flows").Change(Flow(Block("b1", "mute", device: "headphones")));
		await SettleAfterDelayAsync(host);
		host.ById("root.properties.stateProviderOffer.acceptStateProvider").Activate();

		Assert.That(StateIds(host), Is.EqualTo(new[] { "headphones" }).AsCollection);
	}

	[Test]
	public async Task An_icon_provider_that_has_no_icon_yet_says_so_next_to_the_preview_note()
	{
		_cover.Icon = null;
		var host = Render(new { flows = Flow(Block("b1", "cover")) });

		host.ById("flows").Raise(UiConfigEvents.Provide, new { capability = "icon", blockId = "b1", enabled = true });
		await host.SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(host.ToCanonicalJson(), Does.Contain("Widgets.Editor.IconProviderNoIconYet"));
			Assert.That(host.ToCanonicalJson(), Does.Contain("Widgets.Editor.IconProviderPreviewNote"));
		});
	}

	[Test]
	public async Task Adding_an_action_that_provides_both_offers_both_and_applies_only_what_is_kept()
	{
		var host = Render(new { flows = Flow() });

		host.ById("flows").Change(Flow(Block("b1", "now-playing")));
		await SettleAfterDelayAsync(host);

		host.ById("combinedProviderUseIcon").Change(false);
		host.ById("root.properties.combinedProviderOffer.acceptCombinedProvider").Activate();

		Assert.Multiple(() =>
		{
			Assert.That(ProviderBlockId(host), Is.EqualTo("b1"));
			Assert.That(host.ById("iconProvider").Property(UiConfigProperties.Value)!.Value.ValueKind,
				Is.EqualTo(JsonValueKind.Null));
		});
	}

	[Test]
	public async Task Removing_the_provider_action_from_the_flows_restores_the_manual_states_and_says_so()
	{
		var host = Render(new
		{
			stateMode = true,
			states = new object[] { new { id = "off", label = "Off" } },
			flows = Flow(Block("b1", "mute")),
		});
		host.ById("flows").Raise(UiConfigEvents.Provide, new { capability = "state", blockId = "b1", enabled = true });
		await host.SettleAsync();

		host.ById("flows").Change(Flow());

		Assert.Multiple(() =>
		{
			Assert.That(ProviderBlockId(host), Is.Null);
			Assert.That(StateIds(host), Is.EqualTo(new[] { "off" }).AsCollection);
			Assert.That(host.ToCanonicalJson(), Does.Contain("Widgets.Editor.ProviderActionRemoved"));
		});
	}

	[Test]
	public void A_saved_provider_whose_action_is_already_gone_is_repaired_when_the_editor_opens()
	{
		var host = Render(new
		{
			stateMode = true,
			states = new object[] { new { id = "muted", label = "Muted" } },
			stateProvider = new { blockId = "gone", integrationId = IntegrationId, actionId = "mute", actionLabel = "Mute" },
			manualStateBackup = new { states = new object[] { new { id = "off", label = "Off" } } },
			iconProvider = new { blockId = "gone", integrationId = IntegrationId, actionId = "cover" },
			flows = Flow(),
		});

		Assert.Multiple(() =>
		{
			Assert.That(host.ById("stateProvider").Property(UiConfigProperties.Value)!.Value.ValueKind,
				Is.EqualTo(JsonValueKind.Null));
			Assert.That(host.ById("iconProvider").Property(UiConfigProperties.Value)!.Value.ValueKind,
				Is.EqualTo(JsonValueKind.Null));
			Assert.That(StateIds(host), Is.EqualTo(new[] { "off" }).AsCollection);
		});
	}

	[Test]
	public async Task Changing_the_provider_actions_parameters_refreshes_the_provided_states_once_they_settle()
	{
		_mute.Answer = (parameters, _) => Task.FromResult<ActionStateSnapshot?>(
			parameters.GetValueOrDefault("device") as string == "speakers"
				? ControllableStateProviderAction.Snapshot(new ActionStateDefinition("speakers-on", "Speakers on"))
				: ControllableStateProviderAction.Snapshot(new ActionStateDefinition("unmuted", "Unmuted")));
		var host = Render(new { flows = Flow(Block("b1", "mute")) });
		host.ById("flows").Raise(UiConfigEvents.Provide, new { capability = "state", blockId = "b1", enabled = true });
		await host.SettleAsync();
		var callsAfterAdoption = _mute.Calls;

		host.ById("flows").Change(Flow(Block("b1", "mute", device: "head")));
		host.ById("flows").Change(Flow(Block("b1", "mute", device: "headph")));
		host.ById("flows").Change(Flow(Block("b1", "mute", device: "speakers")));
		await SettleAfterDelayAsync(host);

		Assert.Multiple(() =>
		{
			Assert.That(_mute.Calls - callsAfterAdoption, Is.EqualTo(1), "a burst of edits probes once");
			Assert.That(StateIds(host), Is.EqualTo(new[] { "speakers-on" }).AsCollection);
		});
	}

	[Test]
	public async Task A_refresh_answer_for_parameters_that_changed_again_meanwhile_is_discarded()
	{
		var slowSpeakers = new TaskCompletionSource<ActionStateSnapshot?>();
		var host = Render(new { flows = Flow(Block("b1", "mute")) });
		host.ById("flows").Raise(UiConfigEvents.Provide, new { capability = "state", blockId = "b1", enabled = true });
		await host.SettleAsync();
		_mute.Answer = (parameters, _) => parameters.GetValueOrDefault("device") as string == "speakers"
			? slowSpeakers.Task
			: Task.FromResult<ActionStateSnapshot?>(
				ControllableStateProviderAction.Snapshot(new ActionStateDefinition("headphones", "Headphones")));

		host.ById("flows").Change(Flow(Block("b1", "mute", device: "speakers")));
		_time.Advance(ActionButtonConfigContext.SettleDelay);
		host.ById("flows").Change(Flow(Block("b1", "mute", device: "headphones")));
		slowSpeakers.SetResult(ControllableStateProviderAction.Snapshot(new ActionStateDefinition("speakers", "Speakers")));
		await SettleAfterDelayAsync(host);

		Assert.That(StateIds(host), Is.EqualTo(new[] { "headphones" }).AsCollection);
	}

	[Test]
	public async Task A_refresh_and_an_offer_started_together_both_apply()
	{
		var host = Render(new { flows = Flow(Block("b1", "mute")) });
		host.ById("flows").Raise(UiConfigEvents.Provide, new { capability = "state", blockId = "b1", enabled = true });
		await host.SettleAsync();
		_mute.Answer = (_, _) => Task.FromResult<ActionStateSnapshot?>(
			ControllableStateProviderAction.Snapshot(new ActionStateDefinition("refreshed", "Refreshed")));

		host.ById("flows").Change(Flow(Block("b1", "mute", device: "speakers")));
		host.ById("flows").Change(Flow(Block("b1", "mute", device: "speakers"), Block("b2", "cover")));
		await SettleAfterDelayAsync(host);

		Assert.Multiple(() =>
		{
			Assert.That(StateIds(host), Is.EqualTo(new[] { "refreshed" }).AsCollection);
			Assert.That(host.ByType("button").Select(button => button.Id), Has.Some.Contains("acceptIconProvider"));
		});
	}

	private UiTestHost Render(object data, WidgetStateOption? liveState = null)
	{
		var context = new ActionButtonConfigContext(new ActionProviderProbe(_registry,
				new RemoteIconProviderActionRegistry(null!, null!, null!),
				_time,
				Serilog.Core.Logger.None),
			TestLocalization.Resolver,
			"en",
			_time,
			CancellationToken.None);
		var view = new UiView(new UiSurface { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
			ActionButtonWidgetConfigView.Build(JsonSerializer.SerializeToElement(data), 1, _registry, new NoFonts(), liveState, context));
		context.Attach(view);

		return UiTestHost.Render(view);
	}

	private async Task SettleAfterDelayAsync(UiTestHost host)
	{
		_time.Advance(ActionButtonConfigContext.SettleDelay);
		await host.SettleAsync();
	}

	private static object[] Flow(params object[] blocks)
		=> [new { id = "flow-1", triggerType = "short-press", children = blocks }];

	private static object Block(string id, string actionId, bool disabled = false, string? device = null, string label = "")
		=> new
		{
			id,
			integrationId = IntegrationId,
			actionId,
			label,
			disabled,
			parameters = device is null ? Array.Empty<object>() : new object[] { new { name = "device", value = device } },
		};

	private static string? ProviderBlockId(UiTestHost host)
		=> host.ById("stateProvider").Property(UiConfigProperties.Value) is { ValueKind: JsonValueKind.Object } provider
			? provider.GetProperty("blockId").GetString()
			: null;

	private static List<string?> StateIds(UiTestHost host)
		=> host.ById("states").Property(UiConfigProperties.Value)!.Value.EnumerateArray()
			.Select(state => state.GetProperty("id").GetString())
			.ToList();

	private static List<string?> Events(UiTestNode node)
		=> node.Property(UiConfigProperties.Events)!.Value.EnumerateArray().Select(e => e.GetString()).ToList();

	private sealed class NoFonts : IFontCatalog
	{
		public IReadOnlyList<FontFaceInfo> GetFaces() => [];

		public byte[]? GetFaceFile(string faceId) => null;
	}
}
