using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Testing;
using MacroDeck.Ui.Components;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Widgets.ActionButton;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

[TestFixture]
public class ActionButtonWidgetSessionTests
{
	private static readonly Guid _widgetId = Guid.NewGuid();

	private const string TwoStateData =
		"{\"stateMode\":true,\"activeStateId\":\"a\",\"states\":[" +
		"{\"id\":\"a\",\"label\":\"A\",\"appearance\":{\"label\":\"Off\",\"backgroundColor\":\"#111111\"}}," +
		"{\"id\":\"b\",\"label\":\"B\",\"appearance\":{\"label\":\"On\",\"backgroundColor\":\"#222222\"}}]," +
		"\"flows\":\"[]\"}";

	private const string LiquidLabelData = "{\"stateMode\":false,\"label\":\"Vol: {{ vol }}%\"}";

	// No "activeStateId" at all - a freshly placed two-state button, never explicitly advanced or set.
	private const string TwoStateDataNoStoredActiveId =
		"{\"stateMode\":true,\"states\":[" +
		"{\"id\":\"a\",\"label\":\"A\",\"appearance\":{\"label\":\"Off\",\"backgroundColor\":\"#111111\"}}," +
		"{\"id\":\"b\",\"label\":\"B\",\"appearance\":{\"label\":\"On\",\"backgroundColor\":\"#222222\"}}]," +
		"\"flows\":\"[]\"}";

	private static readonly string[] _textOnly = ["text"];

	[Test]
	public async Task A_press_on_a_two_state_button_patches_every_property_that_changed_and_only_that()
	{
		var fixture = Build(TwoStateData, interactive: true);

		fixture.Host.ClearPatches();
		fixture.Host.ById("actionButton").Raise(UiComponentEvents.Press);
		await fixture.Host.SettleAsync();

		var ops = fixture.Host.Patches.SelectMany(p => p.Operations).Select(o => o.Op).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(ops, Is.Not.Empty);
			Assert.That(ops, Has.All.EqualTo(UiPatchOperations.SetProperties), "a state flip must never be structural");
			Assert.That(fixture.Host.ById("actionButton.label").Text("text"), Is.EqualTo("On"));
			Assert.That(fixture.Host.ById("actionButton").Text("background"), Is.EqualTo("#222222"));
			Assert.That(fixture.Trigger.Calls.Single().TriggerType, Is.EqualTo("onShortPress"));
		});

		await fixture.Session.DisposeAsync();
	}

	[Test]
	public async Task A_first_press_with_no_stored_active_state_lands_on_the_same_state_the_persisted_rule_would()
	{
		// Regression for finding 4: the session's own InitialStateId already defaults to states[0] so the
		// tree has something to render, but ActionButtonStateJson's persisted advance treats "nothing ever
		// stored" as null, not states[0] - so its own first advance lands on states[0], not states[1]. The
		// session's optimistic advance has to agree, or the tile paints one state ahead of what gets saved.
		var fixture = Build(TwoStateDataNoStoredActiveId, interactive: true);

		var persistedData = (JsonNode.Parse(TwoStateDataNoStoredActiveId) as JsonObject)!;
		var persistedNext = ActionButtonStateJson.AdvanceState(persistedData);

		fixture.Host.ById("actionButton").Raise(UiComponentEvents.Press);
		await fixture.Host.SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(persistedNext, Is.EqualTo("a"), "sanity: the persisted rule itself stays on state a");
			Assert.That(fixture.Host.ById("actionButton.label").Text("text"), Is.EqualTo("Off"), "state a's label");
			Assert.That(fixture.Host.ById("actionButton").Text("background"),
				Is.EqualTo("#111111"),
				"state a's background");
		});

		await fixture.Session.DisposeAsync();
	}

	[Test]
	public async Task A_long_press_does_not_advance_state_but_still_dispatches_the_trigger()
	{
		var data = TwoStateData.Replace("\"flows\":\"[]\"",
			"\"flows\":\"[{\\\"triggerType\\\":\\\"onLongPress\\\",\\\"children\\\":[]}]\"");
		var fixture = Build(data, interactive: true);

		var result = fixture.Host.ById("actionButton").Raise(UiComponentEvents.LongPress);
		await fixture.Host.SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(result.IsAccepted, Is.True);
			Assert.That(fixture.Host.ById("actionButton.label").Text("text"),
				Is.EqualTo("Off"),
				"no advance on a long press");
			Assert.That(fixture.Trigger.Calls.Single().TriggerType, Is.EqualTo("onLongPress"));
		});

		await fixture.Session.DisposeAsync();
	}

	[Test]
	public async Task A_dispatch_while_locked_is_rejected_and_runs_no_trigger()
	{
		var fixture = Build(TwoStateData, interactive: true, locked: true);

		var result = fixture.Host.ById("actionButton").Raise(UiComponentEvents.Press);
		await fixture.Host.SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(result.IsAccepted, Is.False);
			Assert.That(fixture.Host.ById("actionButton.label").Text("text"), Is.EqualTo("Off"));
			Assert.That(fixture.Trigger.Calls, Is.Empty);
		});

		await fixture.Session.DisposeAsync();
	}

	[Test]
	public async Task A_press_refused_by_a_locked_host_emits_a_failed_action_execution_status_event()
	{
		var fixture = Build(TwoStateData, interactive: true, locked: true);

		fixture.Session.Dispatch(new UiEvent { NodeId = "actionButton", Name = UiComponentEvents.Press }, "client-1");
		await fixture.Host.SettleAsync();

		var evt = fixture.Transport.SentToGroup.OfType<ActionExecutionStatusEvent>().Single();

		Assert.Multiple(() =>
		{
			Assert.That(fixture.Transport.LastGroup, Is.EqualTo(UiClientGroups.For("client-1")));
			Assert.That(evt.Status, Is.EqualTo(ActionExecutionStatus.Failed));
			Assert.That(evt.Error?.Code, Is.EqualTo(ActionExecutionErrorCodes.HostLocked));
			Assert.That(evt.WidgetId, Is.EqualTo(_widgetId.ToString()));
		});

		await fixture.Session.DisposeAsync();
	}

	[Test]
	public async Task A_press_whose_flow_fails_emits_a_failed_action_execution_status_event()
	{
		var fixture = Build(TwoStateData, interactive: true);
		fixture.Trigger.Result = new FlowExecutionResult
		{
			ExecutionId = Guid.NewGuid(),
			Status = FlowExecutionStatus.Failed,
			ErrorCode = "FLOW_ERROR",
			ErrorMessage = MacroDeck.Localization.LocalizedText.FromLiteral("boom")
		};

		fixture.Session.Dispatch(new UiEvent { NodeId = "actionButton", Name = UiComponentEvents.Press }, "client-1");
		await fixture.Host.SettleAsync();

		var evt = fixture.Transport.SentToGroup.OfType<ActionExecutionStatusEvent>().Single();

		Assert.Multiple(() =>
		{
			Assert.That(fixture.Transport.LastGroup, Is.EqualTo(UiClientGroups.For("client-1")));
			Assert.That(evt.Status, Is.EqualTo(ActionExecutionStatus.Failed));
			Assert.That(evt.Error?.Code, Is.EqualTo("FLOW_ERROR"));
		});

		await fixture.Session.DisposeAsync();
	}

	[Test]
	public async Task A_successful_press_emits_no_action_execution_status_event()
	{
		var fixture = Build(TwoStateData, interactive: true);
		fixture.Trigger.Result = new FlowExecutionResult
			{ ExecutionId = Guid.NewGuid(), Status = FlowExecutionStatus.Succeeded };

		fixture.Session.Dispatch(new UiEvent { NodeId = "actionButton", Name = UiComponentEvents.Press }, "client-1");
		await fixture.Host.SettleAsync();

		Assert.That(fixture.Transport.SentToGroup, Is.Empty);

		await fixture.Session.DisposeAsync();
	}

	[Test]
	public async Task A_preview_session_declares_no_events_and_ignores_a_press()
	{
		var fixture = Build(TwoStateData, interactive: false);

		Assert.That(fixture.Host.ById("actionButton").HasProperty("events"), Is.False);

		var result = fixture.Host.ById("actionButton").Raise(UiComponentEvents.Press);
		await fixture.Host.SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(result.IsAccepted, Is.False, "a Preview session declares no handler for press at all");
			Assert.That(fixture.Trigger.Calls, Is.Empty);
			Assert.That(fixture.Host.Patches, Is.Empty);
		});

		await fixture.Session.DisposeAsync();
	}

	[Test]
	public async Task An_unknown_event_name_and_a_press_at_the_label_node_both_run_no_trigger()
	{
		var fixture = Build(TwoStateData, interactive: true);

		var unknown = fixture.Host.ById("actionButton").Raise("double-press");
		var atLabel = fixture.Host.ById("actionButton.label").Raise(UiComponentEvents.Press);
		await fixture.Host.SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(unknown.IsAccepted, Is.False);
			Assert.That(atLabel.IsAccepted, Is.False);
			Assert.That(fixture.Trigger.Calls, Is.Empty);
		});

		await fixture.Session.DisposeAsync();
	}

	[Test]
	public async Task A_button_with_no_declared_flows_and_no_state_still_declares_press_only_when_it_can_advance()
	{
		const string singleState = "{\"stateMode\":false}";
		var fixture = Build(singleState, interactive: true);

		Assert.That(fixture.Host.ById("actionButton").HasProperty("events"),
			Is.False,
			"no flow and nothing to advance");

		await fixture.Session.DisposeAsync();
	}

	[Test]
	public async Task A_state_signal_from_any_authority_patches_the_open_session_with_only_set_properties()
	{
		var fixture = Build(TwoStateData, interactive: true);

		fixture.Host.ClearPatches();
		fixture.Signals.RaiseStateChanged(new WidgetStateUpdatedEvent
		{
			WidgetId = _widgetId.ToString(),
			StateId = "b",
			StateLabel = MacroDeck.Localization.LocalizedText.FromLiteral("B")
		});
		await fixture.Host.SettleAsync();

		var ops = fixture.Host.Patches.SelectMany(p => p.Operations).Select(o => o.Op).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(ops, Is.Not.Empty);
			Assert.That(ops, Has.All.EqualTo(UiPatchOperations.SetProperties));
			Assert.That(fixture.Host.ById("actionButton").Text("background"), Is.EqualTo("#222222"));
		});

		await fixture.Session.DisposeAsync();
	}

	[Test]
	public async Task A_data_changed_signal_shows_a_label_the_widget_did_not_have_when_the_session_opened()
	{
		// A button is created with no label and gets one on the editor's first save, so the deck tile's
		// session is already open - against the empty original - when the label arrives. A plain label
		// has no label subscription to correct it later, so if the data change does not carry it the
		// tile renders no label for as long as that session lives.
		const string unlabelled = "{\"stateMode\":false}";
		var fixture = Build(unlabelled, interactive: true);

		fixture.Host.ClearPatches();
		fixture.Signals.RaiseDataChanged(new WidgetEntity
		{
			Id = _widgetId,
			Type = WidgetTypeIds.ActionButton,
			Data = "{\"label\":\"Fresh Label\",\"stateMode\":false}"
		});
		await fixture.Host.SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(fixture.Host.ById("actionButton.label").Text("text"), Is.EqualTo("Fresh Label"));
			Assert.That(fixture.Host.Patches.SelectMany(p => p.Operations).Select(o => o.Op),
				Has.All.EqualTo(UiPatchOperations.SetProperties));
		});

		await fixture.Session.DisposeAsync();
	}

	[Test]
	public async Task A_data_changed_signal_repaints_appearance_that_a_plugin_action_changed()
	{
		var fixture = Build(TwoStateData, interactive: true);

		fixture.Host.ClearPatches();
		var recoloured = TwoStateData.Replace("#111111", "#abcdef");
		fixture.Signals.RaiseDataChanged(new WidgetEntity
			{ Id = _widgetId, Type = WidgetTypeIds.ActionButton, Data = recoloured });
		await fixture.Host.SettleAsync();

		var ops = fixture.Host.Patches.SelectMany(p => p.Operations).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(ops, Is.Not.Empty);
			Assert.That(ops.Select(o => o.Op), Has.All.EqualTo(UiPatchOperations.SetProperties));
			Assert.That(fixture.Host.ById("actionButton").Text("background"), Is.EqualTo("#abcdef"));
		});

		await fixture.Session.DisposeAsync();
	}

	[Test]
	public async Task
		A_data_changed_signal_referencing_an_icon_this_session_has_never_resolved_serves_it_as_the_source()
	{
		var icons = new FakeWidgetIconResources();
		var fixture = Build(TwoStateData, interactive: true, iconResources: icons);

		fixture.Host.ClearPatches();
		var withNewIcon = TwoStateData.Replace("\"appearance\":{\"label\":\"Off\",\"backgroundColor\":\"#111111\"}",
			"\"appearance\":{\"label\":\"Off\",\"backgroundColor\":\"#111111\",\"iconId\":\"icon-new\"}");
		fixture.Signals.RaiseDataChanged(new WidgetEntity
			{ Id = _widgetId, Type = WidgetTypeIds.ActionButton, Data = withNewIcon });
		await fixture.Session.WaitForIconResolutionAsync();
		await fixture.Host.SettleAsync();

		var ops = fixture.Host.Patches.SelectMany(p => p.Operations).ToList();
		var button = fixture.Host.ById("actionButton");

		Assert.Multiple(() =>
		{
			Assert.That(icons.ResolvedIconIds, Does.Contain("icon-new"));
			Assert.That(ops, Is.Not.Empty);
			Assert.That(ops.Select(o => o.Op),
				Has.All.EqualTo(UiPatchOperations.SetProperties),
				"an icon resolving late is still a set-properties patch, never a structural reconcile");
			Assert.That(button.Property("source")!.Value.GetProperty("resourceId").GetString(),
				Is.EqualTo("resource-icon-new"));
		});

		await fixture.Session.DisposeAsync();
	}

	[Test]
	public async Task An_icon_invalidation_reaching_an_open_session_re_resolves_an_icon_it_already_serves()
	{
		// Regression for finding 7: ResolveMissingIconsAsync only ever resolves an id absent from
		// _iconResourcesState, so a re-imported icon this session already resolved once would otherwise
		// keep drawing its old bytes for the rest of the session's lifetime - WidgetIconResourceInvalidationHandler's
		// evict alone stops a future resolve from handing out the stale handle, but does nothing for a
		// handle already served. IWidgetRenderSignals.RaiseIconInvalidated is what has to reach this
		// open session and drop its own resolved entry so the icon becomes "missing" again.
		var iconId = Guid.NewGuid();
		var icons = new FakeWidgetIconResources();
		var signals = new RaceableRenderSignals();
		var withIcon = TwoStateData.Replace("\"appearance\":{\"label\":\"Off\",\"backgroundColor\":\"#111111\"}",
			$"\"appearance\":{{\"label\":\"Off\",\"backgroundColor\":\"#111111\",\"iconId\":\"{iconId}\"}}");
		var fixture = Build(withIcon, interactive: true, iconResources: icons, renderSignals: signals);

		// Resolve it once, via the same path a stored-data change already uses in production.
		fixture.Signals.RaiseDataChanged(new WidgetEntity
			{ Id = _widgetId, Type = WidgetTypeIds.ActionButton, Data = withIcon });
		await fixture.Session.WaitForIconResolutionAsync();
		Assert.That(icons.ResolvedIconIds, Does.Contain(iconId.ToString()));

		icons.ResolvedIconIds.Clear();
		signals.RaiseIconInvalidated(iconId);
		await fixture.Session.WaitForIconResolutionAsync();

		Assert.That(icons.ResolvedIconIds,
			Does.Contain(iconId.ToString()),
			"an already-resolved icon must be re-resolved once invalidated, not only an id never seen before");

		await fixture.Session.DisposeAsync();
	}

	[Test]
	public async Task An_icon_invalidation_for_an_icon_this_button_does_not_reference_is_ignored()
	{
		var icons = new FakeWidgetIconResources();
		var signals = new RaceableRenderSignals();
		var fixture = Build(TwoStateData, interactive: true, iconResources: icons, renderSignals: signals);

		signals.RaiseIconInvalidated(Guid.NewGuid());
		await fixture.Session.WaitForIconResolutionAsync();

		Assert.That(icons.ResolvedIconIds, Is.Empty);

		await fixture.Session.DisposeAsync();
	}

	[Test]
	public async Task An_icon_id_that_fails_to_resolve_leaves_the_button_with_no_source_property_at_all()
	{
		var icons = new FakeWidgetIconResources();
		icons.FailingIconIds.Add("icon-bad");
		var fixture = Build(TwoStateData, interactive: true, iconResources: icons);

		var withBadIcon = TwoStateData.Replace("\"appearance\":{\"label\":\"Off\",\"backgroundColor\":\"#111111\"}",
			"\"appearance\":{\"label\":\"Off\",\"backgroundColor\":\"#111111\",\"iconId\":\"icon-bad\"}");
		fixture.Signals.RaiseDataChanged(new WidgetEntity
			{ Id = _widgetId, Type = WidgetTypeIds.ActionButton, Data = withBadIcon });
		await fixture.Session.WaitForIconResolutionAsync();
		await fixture.Host.SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(icons.ResolvedIconIds, Does.Contain("icon-bad"));
			Assert.That(fixture.Host.ById("actionButton").HasProperty("source"), Is.False);
		});

		await fixture.Session.DisposeAsync();
	}

	[Test]
	public async Task A_data_changed_signal_that_changes_the_declared_trigger_set_faults_the_session()
	{
		var fixture = Build(TwoStateData, interactive: true);

		UiSessionFaultedEventArgs? fault = null;
		fixture.Session.Faulted += (_, args) => fault = args;

		var withLongPressFlow = TwoStateData.Replace("\"flows\":\"[]\"",
			"\"flows\":\"[{\\\"triggerType\\\":\\\"onLongPress\\\",\\\"children\\\":[]}]\"");
		fixture.Signals.RaiseDataChanged(new WidgetEntity
		{
			Id = _widgetId, Type = WidgetTypeIds.ActionButton, Data = withLongPressFlow
		});

		Assert.That(fault, Is.Not.Null, "adding a declared trigger is structural and must fault the session");

		await fixture.Session.DisposeAsync();
	}

	[Test]
	public async Task A_liquid_label_resolves_host_side_and_the_raw_template_never_reaches_the_tree()
	{
		var fixture = Build(LiquidLabelData, interactive: true, initialLabel: "Vol: 30%");

		Assert.That(fixture.Host.ById("actionButton.label").Text("text"), Is.EqualTo("Vol: 30%"));
		Assert.That(fixture.Host.ToCanonicalJson(), Does.Not.Contain("{{"));
		Assert.That(fixture.LabelSubscriptions.HasSubscribers(_widgetId.ToString(), "off"),
			Is.True,
			"a Liquid label registers the label subscription so a variable change is pushed to it");

		fixture.Host.ClearPatches();
		fixture.Signals.RaiseLabelChanged(new LabelTextUpdatedEvent
			{ WidgetId = _widgetId.ToString(), State = "off", Text = "Vol: 40%" });
		await fixture.Host.SettleAsync();

		var ops = fixture.Host.Patches.SelectMany(p => p.Operations).ToList();

		Assert.Multiple(() =>
		{
			// The label element is shared between the button and its one-step stack Fallback, so both
			// materialized copies patch - but each is still only a "text" change, never structural.
			Assert.That(ops, Has.Count.EqualTo(2));
			Assert.That(ops, Has.All.Matches<UiPatchOperation>(op => op.Op == UiPatchOperations.SetProperties));
			Assert.That(ops, Has.All.Matches<UiPatchOperation>(op => op.Properties!.Keys.SequenceEqual(_textOnly)));
			Assert.That(fixture.Host.ById("actionButton.label").Text("text"), Is.EqualTo("Vol: 40%"));
		});

		await fixture.Session.DisposeAsync();
	}

	[Test]
	public async Task Disposing_a_session_removes_both_tracker_registrations()
	{
		var fixture = Build(LiquidLabelData, interactive: true, initialLabel: "Vol: 30%");
		var widgetKey = _widgetId.ToString();

		Assert.Multiple(() =>
		{
			Assert.That(fixture.StateSubscriptions.HasSubscribers(widgetKey), Is.True);
			Assert.That(fixture.LabelSubscriptions.HasAnySubscribers(widgetKey), Is.True);
		});

		await fixture.Session.DisposeAsync();

		Assert.Multiple(() =>
		{
			Assert.That(fixture.StateSubscriptions.HasSubscribers(widgetKey), Is.False);
			Assert.That(fixture.LabelSubscriptions.HasAnySubscribers(widgetKey),
				Is.False,
				"a widget with no remaining subscriber must stop being treated as live");
		});
	}

	[Test]
	public async Task A_state_signal_that_arrives_after_dispose_never_re_adds_a_label_subscription()
	{
		// Regression for finding 5: WidgetRenderSignals.Raise invokes its handlers outside its own lock,
		// so a push already in flight when DisposeAsync runs can still call this session's OnStateSignal -
		// which re-keys the label subscription via RekeyLabelSubscription - after the subscription
		// object's own Dispose() has already returned. A fake IWidgetRenderSignals whose subscription
		// Dispose is a no-op reproduces that race deterministically: it is exactly what a handler already
		// captured by Raise before unsubscription would still be able to call. Without the _disposed
		// barrier, this late state push re-adds a fresh (and now unowned) entry under the new state -
		// an orphan that keeps HasSubscribers true for the widget's whole remaining process lifetime.
		var signals = new RaceableRenderSignals();
		var fixture = Build(LiquidLabelData, interactive: true, initialLabel: "Vol: 30%", renderSignals: signals);
		var widgetKey = _widgetId.ToString();

		Assert.That(fixture.LabelSubscriptions.HasAnySubscribers(widgetKey), Is.True);

		await fixture.Session.DisposeAsync();

		// Any state id resolves to the same Liquid root label here (stateMode is false), so this always
		// re-keys to a state group this session was never actually subscribed to.
		signals.RaiseStateChanged(new WidgetStateUpdatedEvent { WidgetId = widgetKey, StateId = "on" });

		Assert.That(fixture.LabelSubscriptions.HasAnySubscribers(widgetKey),
			Is.False,
			"a signal handler must never be able to register a new subscription once disposal has begun");
	}

	/// <summary>An <see cref="IWidgetRenderSignals" /> whose subscription <c>Dispose()</c> does not stop
	/// the handler from being called - reproducing, deterministically and without real threads, exactly
	/// what a <c>WidgetRenderSignals.Raise</c> call already in flight when a subscriber disposes can still
	/// do: invoke a handler the subscriber considers gone.</summary>
	private sealed class RaceableRenderSignals : IWidgetRenderSignals
	{
		private Action<WidgetStateUpdatedEvent>? _stateHandler;
		private Action<LabelTextUpdatedEvent>? _labelHandler;
		private Action<WidgetEntity>? _dataHandler;
		private Action<Guid>? _iconInvalidatedHandler;
		private Action? _widgetIconChangedHandler;
		private Action? _variableChangedHandler;

		public IDisposable SubscribeState(string widgetId, Action<WidgetStateUpdatedEvent> handler)
		{
			_stateHandler = handler;
			return new NoopSubscription();
		}

		public IDisposable SubscribeIconInvalidated(Action<Guid> handler)
		{
			_iconInvalidatedHandler = handler;
			return new NoopSubscription();
		}

		public IDisposable SubscribeLabel(string widgetId, Action<LabelTextUpdatedEvent> handler)
		{
			_labelHandler = handler;
			return new NoopSubscription();
		}

		public IDisposable SubscribeDataChanged(string widgetId, Action<WidgetEntity> handler)
		{
			_dataHandler = handler;
			return new NoopSubscription();
		}

		public IDisposable SubscribeWidgetIconChanged(string widgetId, Action handler)
		{
			_widgetIconChangedHandler = handler;
			return new NoopSubscription();
		}

		public IDisposable SubscribeVariableChanged(Action handler)
		{
			_variableChangedHandler = handler;
			return new NoopSubscription();
		}

		public void RaiseStateChanged(WidgetStateUpdatedEvent evt) => _stateHandler?.Invoke(evt);

		public void RaiseLabelChanged(LabelTextUpdatedEvent evt) => _labelHandler?.Invoke(evt);

		public bool RaiseDataChanged(WidgetEntity widget)
		{
			var handler = _dataHandler;
			handler?.Invoke(widget);

			return handler is not null;
		}

		public void RaiseIconInvalidated(Guid iconId) => _iconInvalidatedHandler?.Invoke(iconId);

		public void RaiseWidgetIconChanged(Guid widgetId) => _widgetIconChangedHandler?.Invoke();

		public void RaiseVariableChanged() => _variableChangedHandler?.Invoke();

		private sealed class NoopSubscription : IDisposable
		{
			public void Dispose()
			{
			}
		}
	}

	[Test]
	public async Task
		A_session_with_no_resolved_icon_falls_back_to_a_real_legacy_image_resource_as_the_backdrop_source()
	{
		var imageResource = new UiResource { ResourceId = "legacy-image-1" };
		var fixture = Build(TwoStateData, interactive: true, imageResource: imageResource);

		Assert.That(fixture.Host.ById("actionButton").Property("source")!.Value.GetProperty("resourceId").GetString(),
			Is.EqualTo("legacy-image-1"));

		await fixture.Session.DisposeAsync();
	}

	private static SessionFixture Build(
		string data,
		bool interactive,
		bool locked = false,
		string? initialLabel = null,
		IWidgetIconResources? iconResources = null,
		IWidgetRenderSignals? renderSignals = null,
		UiResource? imageResource = null,
		FakeWidgetIconService? iconService = null)
	{
		var config = ActionButtonWidgetData.Parse(JsonDocument.Parse(data).RootElement);
		var configState = new UiState<ActionButtonWidgetData>(config);
		var activeState = new UiState<string?>(config.InitialStateId);
		var labelText = new UiState<string?>(initialLabel ?? config.Resolve(activeState.Peek()).Label);
		var iconResourcesState
			= new UiState<IReadOnlyDictionary<WidgetIconReference, UiResource>>(
				new Dictionary<WidgetIconReference, UiResource>());
		var widget = new WidgetEntity { Id = _widgetId, Type = WidgetTypeIds.ActionButton, Data = data };
		var trigger = new RecordingTriggerService();
		var lockState = new FakeHostLockState { IsLocked = locked };
		var icons = iconResources ?? new FakeWidgetIconResources();
		var stateSubscriptions = new WidgetStateSubscriptionTracker();
		var labelSubscriptions = new LabelSubscriptionTracker();
		var signals = renderSignals ?? new WidgetRenderSignals();
		var transport = new RecordingTransport();
		var iconServiceFake = iconService ?? new FakeWidgetIconService();
		var iconProviderState = new UiState<WidgetIconResolution>(iconServiceFake.Resolution);
		var scopeFactory = new ServiceCollection()
			.AddScoped<IWidgetIconService>(_ => iconServiceFake)
			.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true })
			.GetRequiredService<IServiceScopeFactory>();

		var session = new ActionButtonWidgetSession(configState,
			activeState,
			labelText,
			iconResourcesState,
			iconProviderState,
			widget,
			trigger,
			lockState,
			icons,
			scopeFactory,
			stateSubscriptions,
			labelSubscriptions,
			signals,
			transport,
			interactive);
		var element = ActionButtonWidgetView.Build(configState,
			activeState,
			labelText,
			iconResourcesState,
			iconProviderState,
			imageResource,
			session.BuildEvents());
		var view = new UiView(new UiSurface { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
			element);

		session.Attach(view);

		var host = UiTestHost.Render(view);

		return new SessionFixture(host,
			session,
			trigger,
			signals,
			transport,
			stateSubscriptions,
			labelSubscriptions,
			iconServiceFake);
	}

	private sealed record SessionFixture(
		UiTestHost Host,
		ActionButtonWidgetSession Session,
		RecordingTriggerService Trigger,
		IWidgetRenderSignals Signals,
		RecordingTransport Transport,
		WidgetStateSubscriptionTracker StateSubscriptions,
		LabelSubscriptionTracker LabelSubscriptions,
		FakeWidgetIconService IconService);

	/// <summary>A configurable <see cref="IWidgetIconService" /> stand-in - defaults to
	/// <see cref="WidgetIconResolution.Inactive" />, matching a button with no icon-provider assignment.</summary>
	internal sealed class FakeWidgetIconService : IWidgetIconService
	{
		public WidgetIconResolution Resolution { get; set; } = WidgetIconResolution.Inactive;

		public TimeSpan? PollInterval { get; set; }

		public int ResolveCallCount { get; private set; }

		public Task<WidgetIconResolution> Resolve(Guid widgetId, CancellationToken cancellationToken = default)
		{
			ResolveCallCount++;
			return Task.FromResult(Resolution);
		}

		public TimeSpan? GetProviderPollInterval(Guid widgetId) => PollInterval;
	}

	/// <summary>Resolves an icon reference to a <see cref="UiResource" /> named after its bare reference
	/// string, unless that reference is registered in <see cref="FailingIconIds" /> - the session's own
	/// <c>null</c>-means-absent contract, exercised without any of <c>WidgetIconResources</c>' real caching
	/// or DI-scope machinery.</summary>
	private sealed class FakeWidgetIconResources : IWidgetIconResources
	{
		public HashSet<string> FailingIconIds { get; } = new(StringComparer.Ordinal);

		public List<string> ResolvedIconIds { get; } = [];

		public Task<UiResource?> ResolveAsync(WidgetIconReference? reference, CancellationToken cancellationToken)
		{
			if (reference is not { } value)
			{
				return Task.FromResult<UiResource?>(null);
			}

			ResolvedIconIds.Add(value.Reference);

			if (FailingIconIds.Contains(value.Reference))
			{
				return Task.FromResult<UiResource?>(null);
			}

			return Task.FromResult<UiResource?>(new UiResource { ResourceId = $"resource-{value.Reference}" });
		}

		public void Evict(Guid iconId)
		{
		}
	}

	private sealed class RecordingTriggerService : IWidgetTriggerService
	{
		public List<(string TriggerType, string? OriginClientId)> Calls { get; } = [];

		public FlowExecutionResult? Result { get; set; }

		public Task<ActionExecutionDispatch> ExecuteAsync(
			WidgetEntity widget,
			string triggerType,
			string? originClientId,
			Guid? originDeviceId,
			CancellationToken cancellationToken)
		{
			Calls.Add((triggerType, originClientId));

			return Task.FromResult(new ActionExecutionDispatch(Guid.NewGuid(), Result));
		}
	}

	private sealed class RecordingTransport : IUiTransport
	{
		public List<object> SentToGroup { get; } = [];

		public string? LastGroup { get; private set; }

		public Task Send<T>(T message, CancellationToken cancellationToken = default)
			where T : class
			=> Task.CompletedTask;

		public Task SendToGroup<T>(string group, T message, CancellationToken cancellationToken = default)
			where T : class
		{
			LastGroup = group;
			SentToGroup.Add(message);

			return Task.CompletedTask;
		}

		public Task SendToConnection<T>(string connectionId, T message, CancellationToken cancellationToken = default)
			where T : class
			=> Task.CompletedTask;

		public Task AddToGroup(string connectionId, string group, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task RemoveFromGroup(string connectionId, string group, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;
	}
}
