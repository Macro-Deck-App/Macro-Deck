using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Timers;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Widgets.Timers;

namespace MacroDeckHost.Tests.UnitTests.Timers;

public class TimerWidgetUiTests
{
	private TimerWidgetHarness _harness = null!;
	private RecordingUiTransport _transport = null!;
	private CountdownWidgetUiProvider _countdowns = null!;
	private StopwatchWidgetUiProvider _stopwatches = null!;
	private CountdownDurationDrafts _drafts = null!;

	[SetUp]
	public void SetUp()
	{
		_harness = new TimerWidgetHarness();
		_transport = new RecordingUiTransport();
		_drafts = new CountdownDurationDrafts();
		var sessions = new TimerWidgetSessionFactory(_harness.Coordinator, _harness.LockState, _transport);
		_countdowns = new CountdownWidgetUiProvider(sessions, _drafts);
		_stopwatches = new StopwatchWidgetUiProvider(sessions);
	}

	[TearDown]
	public async Task TearDown() => await _harness.DisposeAsync();

	[Test]
	public async Task A_placed_timer_answers_both_a_tap_and_a_hold_itself()
	{
		var countdown = await _harness.AddCountdownAsync();
		var stopwatch = await _harness.AddStopwatchAsync();

		await using var countdownSession = await OpenAsync(_countdowns, countdown, UiSurfaceKinds.Widget);
		await using var stopwatchSession = await OpenAsync(_stopwatches, stopwatch, UiSurfaceKinds.Widget);

		Assert.Multiple(() =>
		{
			Assert.That(Events(countdownSession.BuildTree().Root),
				Is.EquivalentTo(new[] { UiComponentEvents.Press, UiComponentEvents.LongPress }));
			Assert.That(Events(stopwatchSession.BuildTree().Root),
				Is.EquivalentTo(new[] { UiComponentEvents.Press, UiComponentEvents.LongPress }));
		});
	}

	[Test]
	public async Task A_tap_on_the_deck_starts_the_countdown_for_the_client_that_tapped_it()
	{
		var widget = await _harness.AddCountdownAsync();
		await using var session = await OpenAsync(_countdowns, widget, UiSurfaceKinds.Widget);

		Press(session, UiComponentEvents.Press, "tablet");
		await TimerWidgetHarness.EventuallyAsync(() => _harness.Triggers.Calls.Count == 1);

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Phase(widget), Is.EqualTo(TimerWidgetPhase.Running));
			Assert.That(_harness.Triggers.Calls.Single().ClientId, Is.EqualTo("tablet"));
		});
	}

	[Test]
	public async Task A_tap_while_the_host_is_locked_is_refused_and_the_client_is_told_why()
	{
		var widget = await _harness.AddCountdownAsync();
		await using var session = await OpenAsync(_countdowns, widget, UiSurfaceKinds.Widget);
		_harness.LockState.IsLocked = true;

		Press(session, UiComponentEvents.Press, "tablet");
		await TimerWidgetHarness.QuietPeriodAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Phase(widget), Is.EqualTo(TimerWidgetPhase.Idle));
			Assert.That(_transport.GroupMessages, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_preview_draws_the_timer_but_never_starts_the_real_one()
	{
		var widget = await _harness.AddCountdownAsync();
		await using var preview = await OpenAsync(_countdowns, widget, UiSurfaceKinds.Preview);

		var events = Events(preview.BuildTree().Root);
		preview.Dispatch(new UiEvent { NodeId = preview.BuildTree().Root.Id, Name = UiComponentEvents.Press });

		Assert.Multiple(() =>
		{
			Assert.That(events, Is.Empty);
			Assert.That(_harness.Phase(widget), Is.EqualTo(TimerWidgetPhase.Idle));
		});
	}

	[Test]
	public async Task The_countdown_finishes_once_however_many_decks_show_it()
	{
		var widget = await _harness.AddCountdownAsync(seconds: 2);
		await using var tablet = await OpenAsync(_countdowns, widget, UiSurfaceKinds.Widget);
		await using var phone = await OpenAsync(_countdowns, widget, UiSurfaceKinds.Widget);

		Press(tablet, UiComponentEvents.Press, "tablet");
		await TimerWidgetHarness.EventuallyAsync(() => _harness.Phase(widget) == TimerWidgetPhase.Running);
		await _harness.AdvanceAsync(TimeSpan.FromSeconds(2));
		await _harness.AdvanceAsync(TimeSpan.FromSeconds(2));

		Assert.That(_harness.FlowExecutor.Calls.Count(call => call.Trigger == WidgetTriggerTypes.CountdownFinished),
			Is.EqualTo(1));
	}

	[Test]
	public async Task Two_decks_pressing_the_same_timer_at_once_never_lock_each_other_up()
	{
		var widget = await _harness.AddStopwatchAsync();
		await using var tablet = await OpenAsync(_stopwatches, widget, UiSurfaceKinds.Widget);
		await using var phone = await OpenAsync(_stopwatches, widget, UiSurfaceKinds.Widget);

		var presses = Task.WhenAll(
			Task.Run(() =>
			{
				for (var index = 0; index < 200; index++)
				{
					Press(tablet, UiComponentEvents.Press, "tablet");
				}
			}),
			Task.Run(() =>
			{
				for (var index = 0; index < 200; index++)
				{
					Press(phone, UiComponentEvents.LongPress, "phone");
				}
			}));

		Assert.That(await Task.WhenAny(presses, Task.Delay(TimeSpan.FromSeconds(10))), Is.SameAs(presses));
	}

	[Test]
	public async Task A_finished_countdown_is_drawn_with_the_animated_alert_ring()
	{
		var widget = await _harness.AddCountdownAsync(seconds: 1);
		await using var session = await OpenAsync(_countdowns, widget, UiSurfaceKinds.Widget);
		Press(session, UiComponentEvents.Press, "tablet");
		await TimerWidgetHarness.EventuallyAsync(() => _harness.Phase(widget) == TimerWidgetPhase.Running);

		await _harness.AdvanceAsync(TimeSpan.FromSeconds(1));

		var tree = JsonSerializer.Serialize(session.BuildTree());
		Assert.That(tree, Does.Contain($"\"{UiComponentBorderStyles.Heartbeat}\""));
	}

	[TestCase(0)]
	[TestCase(1)]
	[TestCase(9_000)]
	[TestCase(9_001)]
	[TestCase(9_999)]
	public void The_countdown_readout_rounds_up_exactly_like_the_remaining_seconds_variable(int elapsedMs)
	{
		var snapshot = new TimerWidgetSnapshot
		{
			WidgetId = Guid.NewGuid(),
			Kind = TimerWidgetKind.Countdown,
			Phase = TimerWidgetPhase.Paused,
			DurationMs = 10_000,
			ElapsedMs = elapsedMs,
			Anchor = _harness.Time.Now,
		};

		var progress = TimerFace.From(snapshot, _harness.Time.Now).Progress;

		var drawn = (progress.DurationMs!.Value - Math.Clamp(progress.PositionMs, 0, progress.DurationMs.Value)) / 1000;
		Assert.That(drawn, Is.EqualTo(snapshot.RemainingSecondsAt(_harness.Time.Now)));
	}

	[Test]
	public async Task The_duration_dialog_starts_a_preset_or_the_time_that_was_dialled_in()
	{
		var interactions = new AnsweringUiInteractions();
		var prompt = new CountdownDurationPrompt(interactions, _drafts);

		interactions.Answer = "preset:300";
		var preset = await prompt.AskAsync(Guid.NewGuid(), "tablet", null, CancellationToken.None);
		interactions.Answer = "preset:7";
		var unknownPreset = await prompt.AskAsync(Guid.NewGuid(), "tablet", null, CancellationToken.None);
		interactions.Answer = "start";
		var dialled = await prompt.AskAsync(Guid.NewGuid(), "tablet", 75, CancellationToken.None);
		interactions.Answer = null;
		var cancelled = await prompt.AskAsync(Guid.NewGuid(), "tablet", 75, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(preset, Is.EqualTo(300));
			Assert.That(unknownPreset, Is.Null);
			Assert.That(dialled, Is.EqualTo(75));
			Assert.That(cancelled, Is.Null);
			Assert.That(interactions.OwnerIds, Is.All.EqualTo(CountdownWidgetUiProvider.OwnerId));
		});
	}

	[Test]
	public async Task The_dialog_steps_the_time_the_start_button_will_use()
	{
		var widget = await _harness.AddCountdownAsync(ask: true);
		var draftId = Guid.NewGuid();
		_drafts.Begin(draftId, 60);
		await using var dialog = await OpenDialogAsync(widget, draftId);

		PressNode(dialog, "minutesUpFace");
		PressNode(dialog, "secondsUpFace");

		Assert.That(_drafts.Get(draftId), Is.EqualTo(125));
	}

	[Test]
	public async Task Every_preset_and_the_start_button_can_be_pressed_to_answer_the_dialog()
	{
		var widget = await _harness.AddCountdownAsync(ask: true);
		var draftId = Guid.NewGuid();
		_drafts.Begin(draftId, 60);
		await using var dialog = await OpenDialogAsync(widget, draftId);

		var answering = Nodes(dialog.BuildTree().Root)
			.Where(node => node.Properties.ContainsKey(UiComponentProperties.Answer))
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(answering.Select(node => node.Properties[UiComponentProperties.Answer].GetString()),
				Is.EquivalentTo(new[]
				{
					"preset:60", "preset:300", "preset:600", "preset:900", "preset:1800", "preset:3600", "start",
				}));
			Assert.That(answering.Select(Events), Has.All.Contains(UiComponentEvents.Press));
		});
	}

	[Test]
	public void A_countdown_opens_its_flows_on_times_up_and_a_stopwatch_on_started()
	{
		Assert.Multiple(() =>
		{
			Assert.That(TimerWidgetConfigView.Triggers(isCountdown: true)[0], Is.EqualTo(WidgetTriggerTypes.CountdownFinished));
			Assert.That(TimerWidgetConfigView.Triggers(isCountdown: false)[0], Is.EqualTo(WidgetTriggerTypes.StopwatchStarted));
		});
	}

	[Test]
	public async Task A_dialog_left_open_while_the_countdown_started_elsewhere_says_it_is_already_running()
	{
		var widget = await _harness.AddCountdownAsync(ask: true);
		var draftId = Guid.NewGuid();
		_drafts.Begin(draftId, 60);
		await using var dialog = await OpenDialogAsync(widget, draftId);

		await _harness.Coordinator.StartWithDurationAsync(widget.Id, 30, "phone", null);

		var tree = JsonSerializer.Serialize(dialog.BuildTree());
		Assert.Multiple(() =>
		{
			Assert.That(tree, Does.Not.Contain("\"answer\":\"start\""));
			Assert.That(tree, Does.Contain("AlreadyRunning"));
		});
	}

	private static async Task<IUiSession> OpenAsync(IUiProvider provider, Domain.Entities.WidgetEntity widget, string kind)
	{
		var surface = new UiSurface
		{
			Kind = kind,
			SessionMode = UiSessionModes.Shared,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiWidgetSurfaceAttributes.WidgetId] = JsonSerializer.SerializeToElement(widget.Id.ToString()),
				[UiWidgetSurfaceAttributes.WidgetType] = JsonSerializer.SerializeToElement(widget.Type),
				[UiWidgetSurfaceAttributes.Data] = JsonDocument.Parse(widget.Data!).RootElement.Clone(),
			},
		};

		return (await provider.CreateSessionAsync(new UiSessionRequest { Surface = surface, UiModelVersion = 1 }, CancellationToken.None))!;
	}

	private async Task<IUiSession> OpenDialogAsync(Domain.Entities.WidgetEntity widget, Guid draftId)
	{
		var surface = new UiSurface
		{
			Kind = UiSurfaceKinds.Dialog,
			SessionMode = UiSessionModes.Exclusive,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiDialogSurfaceAttributes.ViewId] =
					JsonSerializer.SerializeToElement(CountdownWidgetUiProvider.DurationDialogViewId),
				[UiDialogSurfaceAttributes.Data] = JsonSerializer.SerializeToElement(new Dictionary<string, string>
				{
					["widgetId"] = widget.Id.ToString(), ["draftId"] = draftId.ToString(),
				}),
			},
		};

		return (await _countdowns.CreateSessionAsync(new UiSessionRequest { Surface = surface, UiModelVersion = 1 }, CancellationToken.None))!;
	}

	private static void Press(IUiSession session, string name, string clientId)
		=> ((IOriginAwareUiSession)session).Dispatch(new UiEvent { NodeId = session.BuildTree().Root.Id, Name = name },
			clientId);

	private static void PressNode(IUiSession session, string key)
	{
		var node = Find(session.BuildTree().Root, key) ?? throw new AssertionException($"No node '{key}' in the tree.");
		session.Dispatch(new UiEvent { NodeId = node.Id, Name = UiComponentEvents.Press });
	}

	private static IEnumerable<UiNode> Nodes(UiNode node)
		=> node.Children.SelectMany(Nodes).Prepend(node);

	private static UiNode? Find(UiNode node, string key)
		=> node.Id.EndsWith(key, StringComparison.Ordinal)
			? node
			: node.Children.Select(child => Find(child, key)).FirstOrDefault(found => found is not null);

	private static IReadOnlyList<string> Events(UiNode node)
		=> node.Properties.TryGetValue(UiComponentProperties.Events, out var events) &&
			events.ValueKind == JsonValueKind.Array
				? [.. events.EnumerateArray().Select(name => name.GetString()!)]
				: [];

	private sealed class AnsweringUiInteractions : Application.Ui.Modals.IUiInteractionsFactory, IUiInteractions
	{
		public string? Answer { get; set; }

		public List<string> OwnerIds { get; } = [];

		public IUiInteractions ForIntegration(string integrationId)
		{
			OwnerIds.Add(integrationId);

			return this;
		}

		public Task<bool> ShowModalAsync(string? originClientId, ModalDefinition modal,
			CancellationToken cancellationToken = default)
			=> Task.FromResult(true);

		public Task<ModalResult<T>> ShowModalAsync<T>(string? originClientId, ModalDefinition modal,
			CancellationToken cancellationToken = default)
			=> Task.FromResult(Answer is null
				? ModalResult.FromCancellation<T>()
				: ModalResult.FromValue((T)(object)Answer));
	}
}
