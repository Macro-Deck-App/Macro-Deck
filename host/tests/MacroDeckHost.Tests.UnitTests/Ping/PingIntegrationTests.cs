using System.Text.Json;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Integrations.Ping;
using MacroDeckHost.Integrations.Ping.Connections;
using MacroDeckHost.Integrations.Ping.Events;
using MacroDeckHost.Integrations.Ping.Protocol;
using MacroDeckHost.Integrations.Ping.State;
using MacroDeckHost.Integrations.Ping.Variables;
using MacroDeckHost.Tests.UnitTests.Calendar;
using MacroDeckHost.Tests.UnitTests.Delegation;

namespace MacroDeckHost.Tests.UnitTests.Ping;

[TestFixture]
internal sealed class PingIntegrationTests
{
	private const string Nas = "nas.lan";
	private static readonly TimeSpan _interval = TimeSpan.FromSeconds(10);

	private FakePingContext _context = null!;
	private FakePingProber _prober = null!;
	private FakeTimeProvider _time = null!;
	private PingIntegration _integration = null!;

	private IPingTargets Hub => _integration.Targets;

	[SetUp]
	public void SetUp()
	{
		_context = new FakePingContext();
		_prober = new FakePingProber();
		_time = new FakeTimeProvider();
		_integration = new PingIntegration(_prober, _time);
	}

	[TearDown]
	public Task TearDown()
	{
		return _integration.ShutdownAsync();
	}

	[Test]
	public async Task A_target_goes_offline_only_after_the_failure_threshold_and_back_online_on_the_first_reply()
	{
		_context.AddTarget("NAS", Nas, "nas");
		_prober.Script(Nas,
			PingProbeResult.Reply(7),
			FakePingProber.Fail(),
			FakePingProber.Fail(),
			FakePingProber.Fail(),
			PingProbeResult.Reply(9));
		await StartAsync();

		await NextPollAsync();
		await NextPollAsync();
		var belowThreshold = Snapshot();
		var latencyBelowThreshold = await ReadAsync("latency");
		var eventsBelowThreshold = _context.Published.Count;

		await NextPollAsync();
		var offline = Snapshot();
		await NextPollAsync();
		var online = Snapshot();

		Assert.Multiple(() =>
		{
			Assert.That(belowThreshold.Reachability, Is.EqualTo(PingReachability.Online));
			Assert.That(latencyBelowThreshold, Is.EqualTo(VariableReading.Of(7L)));
			Assert.That(eventsBelowThreshold, Is.Zero);
			Assert.That(offline.Reachability, Is.EqualTo(PingReachability.Offline));
			Assert.That(online.Reachability, Is.EqualTo(PingReachability.Online));
			Assert.That(online.LatencyMilliseconds, Is.EqualTo(9));
			Assert.That(_context.Published.Select(published => published.EventId),
				Is.EqualTo(new[] { PingEventIds.TargetOffline, PingEventIds.TargetOnline }));
			Assert.That(_context.Published.Last().Payload[PingEventPayload.Latency], Is.EqualTo(9L));
		});
	}

	[Test]
	public async Task The_first_result_after_start_fires_no_event()
	{
		_context.AddTarget("Router", "router.lan", "router", 1);
		_context.AddTarget("NAS", Nas, "nas", 1);
		_prober.Script("router.lan", FakePingProber.Fail());
		await StartAsync(2);

		Assert.Multiple(() =>
		{
			Assert.That(Hub.Targets.Single(target => target.Host == "router.lan").Reachability,
				Is.EqualTo(PingReachability.Offline));
			Assert.That(Hub.Targets.Single(target => target.Host == Nas).Reachability,
				Is.EqualTo(PingReachability.Online));
			Assert.That(_context.Published, Is.Empty);
		});
	}

	[TestCase(PingProblem.TimedOut)]
	[TestCase(PingProblem.Unreachable)]
	[TestCase(PingProblem.NameNotResolved)]
	public async Task A_failing_target_turns_offline_and_keeps_being_pinged(PingProblem problem)
	{
		_context.AddTarget("NAS", Nas, "nas", 2);
		_prober.Fallback = FakePingProber.Fail(problem);
		await StartAsync();

		await NextPollAsync();
		await NextPollAsync();

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(Snapshot().Reachability, Is.EqualTo(PingReachability.Offline));
			Assert.That(Snapshot().Problem, Is.EqualTo(problem));
			Assert.That(_prober.Calls(Nas), Is.EqualTo(3));
			Assert.That(await ReadAsync("online"), Is.EqualTo(VariableReading.Of(false)));
			Assert.That(await ReadAsync("latency"), Is.EqualTo(VariableReading.Unavailable));
			Assert.That(await ReadAsync("packet_loss"), Is.EqualTo(VariableReading.Of(100d)));
		});
	}

	[Test]
	public async Task A_missing_ping_permission_says_nothing_about_the_target()
	{
		_context.AddTarget("NAS", Nas, "nas", 2);
		_prober.Fallback = FakePingProber.Fail(PingProblem.NotPermitted);
		await StartAsync();
		await NextPollAsync();
		await NextPollAsync();

		var issues = await _integration.GetIssuesAsync();

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(Snapshot().Reachability, Is.EqualTo(PingReachability.Unknown));
			Assert.That(await ReadAsync("online"), Is.EqualTo(VariableReading.Unavailable));
			Assert.That(issues, Has.Count.EqualTo(1));
			Assert.That(_context.Published, Is.Empty);
		});
	}

	[Test]
	public async Task A_missing_ping_permission_while_online_neither_counts_as_a_failure_nor_fires_an_event()
	{
		_context.AddTarget("NAS", Nas, "nas", 2);
		_prober.Script(Nas,
			PingProbeResult.Reply(4),
			FakePingProber.Fail(),
			FakePingProber.Fail(PingProblem.NotPermitted),
			FakePingProber.Fail(PingProblem.NotPermitted));
		await StartAsync();

		await NextPollAsync();
		await NextPollAsync();
		await NextPollAsync();

		Assert.Multiple(() =>
		{
			Assert.That(Snapshot().Reachability, Is.EqualTo(PingReachability.Online));
			Assert.That(_context.Published, Is.Empty);
		});
	}

	[Test]
	public async Task Renaming_an_offline_target_keeps_its_state_so_its_return_still_fires()
	{
		var entryId = _context.AddTarget("NAS", Nas, "nas", 1);
		_prober.Script(Nas, PingProbeResult.Reply(4), FakePingProber.Fail());
		await StartAsync();
		await NextPollAsync();
		_prober.Script(Nas, PingProbeResult.Reply(6));

		_context.Rename(entryId, "Storage");
		_context.SetTarget(entryId, Nas, "storage", 1);
		await ReloadAsync();

		Assert.Multiple(() =>
		{
			Assert.That(Snapshot().Title, Is.EqualTo("Storage"));
			Assert.That(Snapshot().Reachability, Is.EqualTo(PingReachability.Online));
			Assert.That(Snapshot().PacketLossPercent, Is.EqualTo(100d / 3).Within(0.1));
			Assert.That(_context.Published.Select(published => published.EventId),
				Is.EqualTo(new[] { PingEventIds.TargetOffline, PingEventIds.TargetOnline }));
			Assert.That(_integration.Variables.Select(variable => variable.Name), Does.Contain("ping_storage_online"));
		});
	}

	[Test]
	public async Task Pointing_a_target_at_another_host_starts_it_over_without_an_event()
	{
		var entryId = _context.AddTarget("NAS", Nas, "nas", 1);
		_prober.Script(Nas, PingProbeResult.Reply(4), FakePingProber.Fail());
		await StartAsync();
		await NextPollAsync();

		_context.SetTarget(entryId, "nas2.lan", "nas", 1);
		await _integration.ReloadConfigurationsAsync(CancellationToken.None);
		await CalendarTree.WaitForAsync(() => _prober.Calls("nas2.lan") == 1
				&& Snapshot().Host == "nas2.lan"
				&& Snapshot().Reachability == PingReachability.Online,
			"the new host was never pinged");

		Assert.That(_context.Published.Select(published => published.EventId),
			Is.EqualTo(new[] { PingEventIds.TargetOffline }));
	}

	[Test]
	public async Task A_lowered_threshold_applies_on_the_next_failure()
	{
		var entryId = _context.AddTarget("NAS", Nas, "nas", 5);
		_prober.Script(Nas,
			PingProbeResult.Reply(4),
			FakePingProber.Fail(),
			FakePingProber.Fail(),
			FakePingProber.Fail());
		await StartAsync();
		await NextPollAsync();
		await NextPollAsync();

		_context.SetTarget(entryId, Nas, "nas", 2);
		await ReloadAsync();

		Assert.Multiple(() =>
		{
			Assert.That(Snapshot().Reachability, Is.EqualTo(PingReachability.Offline));
			Assert.That(_context.Published.Select(published => published.EventId),
				Is.EqualTo(new[] { PingEventIds.TargetOffline }));
		});
	}

	[Test]
	public async Task Variables_are_named_after_the_target_and_read_the_last_reply()
	{
		_context.AddTarget("NAS", Nas, "nas");
		_prober.Script(Nas, PingProbeResult.Reply(12));
		_time.Now = new DateTimeOffset(2026, 3, 4, 10, 0, 0, TimeSpan.FromHours(2));
		await StartAsync();

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(_integration.Variables.Select(variable => variable.Name),
				Is.EquivalentTo(new[]
					{ "ping_nas_online", "ping_nas_latency", "ping_nas_packet_loss", "ping_nas_last_seen" }));
			Assert.That(await ReadAsync("online"), Is.EqualTo(VariableReading.Of(true)));
			Assert.That(await ReadAsync("latency"), Is.EqualTo(VariableReading.Of(12L)));
			Assert.That(await ReadAsync("packet_loss"), Is.EqualTo(VariableReading.Of(0d)));
			Assert.That(await ReadAsync("last_seen"),
				Is.EqualTo(VariableReading.Of("2026-03-04T08:00:00.0000000+00:00")));
		});
	}

	[Test]
	public async Task Widgets_and_triggers_share_one_ping_loop_per_target()
	{
		_context.AddTarget("NAS", Nas, "nas");
		await StartAsync();
		await using var first = await PingWidgetTests.OpenAsync(_integration, new { });
		await using var second = await PingWidgetTests.OpenAsync(_integration, new { target = Snapshot().EntryId });

		await NextPollAsync();
		await NextPollAsync();
		await ReadAsync("online");
		await _integration.GetEventOptionsAsync(new EventOptionsContext
				{ EventId = PingEventIds.TargetOnline, ParameterName = "configuration" },
			CancellationToken.None);

		Assert.That(_prober.Calls(Nas), Is.EqualTo(3));
	}

	[Test]
	public async Task A_trigger_narrowed_to_one_target_ignores_the_other_targets_events()
	{
		var nas = _context.AddTarget("NAS", Nas, "nas", 1);
		var router = _context.AddTarget("Router", "router.lan", "router", 1);
		await StartAsync(2);
		var definition = PingEventDefinitions.All.Single(candidate => candidate.Id == PingEventIds.TargetOffline);
		var fromRouter = PingEventDefinitions.For(Hub.Find(router.ToString("D"))!, PingReachability.Offline);
		var fromNas = PingEventDefinitions.For(Hub.Find(nas.ToString("D"))!, PingReachability.Offline);

		var options = await _integration.GetEventOptionsAsync(
			new EventOptionsContext { EventId = definition.Id, ParameterName = PingEventPayload.Configuration },
			CancellationToken.None);

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(options.Options.Select(option => option.Label.ToString()),
				Is.EquivalentTo(new[] { "NAS", "Router" }));
			Assert.That(await MatchesAsync(definition, fromNas.Payload, nas), Is.True);
			Assert.That(await MatchesAsync(definition, fromRouter.Payload, nas), Is.False);
		});
	}

	[Test]
	public async Task Removing_a_target_deletes_its_variables_and_keeps_the_others()
	{
		var kept = Guid.NewGuid();
		var removed = Guid.NewGuid();
		foreach (var declaration in PingVariables.Declare("nas", kept).Concat(PingVariables.Declare("old", removed)))
		{
			await ((IVariableApi)_context.OwnVariables).CreateAsync(declaration);
		}

		await PingIntegration.SynchronizeEntriesAsync([FakePingContext.Stored(kept, "NAS", "nas")],
			_context.OwnVariables,
			CancellationToken.None);

		Assert.That(_context.OwnVariables.All.Select(variable => variable.Name).Distinct(),
			Is.EquivalentTo(new[]
				{ "ping_nas_online", "ping_nas_latency", "ping_nas_packet_loss", "ping_nas_last_seen" }));
	}

	private PingSnapshot Snapshot()
	{
		return Hub.Targets.Single();
	}

	private ValueTask<VariableReading> ReadAsync(string slot)
	{
		var target = Snapshot();
		return _integration.ReadAsync($"entry-{target.EntryId:N}-{slot.Replace('_', '-')}");
	}

	private async Task StartAsync(int targets = 1)
	{
		await _integration.InitializeAsync(_context);
		await CalendarTree.WaitForAsync(() => Hub.Targets.Count(target
					=> target.Reachability != PingReachability.Unknown
					|| target.Problem != PingProblem.None)
				== targets
				&& _time.ActiveTimerCount == targets,
			"the targets were never pinged");
	}

	private async Task NextPollAsync()
	{
		var before = _prober.Calls(Nas);
		_time.Advance(_interval);
		await CalendarTree.WaitForAsync(() => _prober.Calls(Nas) > before && _time.ActiveTimerCount == 1,
			"the target was not pinged again");
	}

	private async Task ReloadAsync()
	{
		var before = _prober.Calls(Nas);
		await _integration.ReloadConfigurationsAsync(CancellationToken.None);
		await CalendarTree.WaitForAsync(() => _prober.Calls(Nas) > before && _time.ActiveTimerCount == 1,
			"the retuned target was not pinged again");
	}

	private static async Task<bool> MatchesAsync(
		EventDefinition definition,
		IReadOnlyDictionary<string, object?> payload,
		Guid target)
	{
		var eventId = $"{PingIntegration.IntegrationId}::{definition.Id}";
		var descriptor = new EventDefinitionDescriptor(QualifiedId.Parse(eventId),
			PingIntegration.IntegrationId,
			"Ping",
			false,
			definition);
		var subscription = new EventSubscription(EventTriggerOwner.ForWidget(Guid.NewGuid()),
			"t1",
			eventId,
			new Dictionary<string, EventConfigurationValue>(StringComparer.Ordinal)
			{
				[PingEventPayload.Configuration] = new(JsonSerializer.SerializeToElement(target.ToString("D")), "==")
			},
			null);
		var context = await new VariableTemplateRenderer(new VariableRegistry())
			.CreateContextAsync(VariableScope.Global, null);

		return new EventSubscriptionMatcher(
				new ActionConditionEvaluator(new VariableTemplateRenderer(new VariableRegistry())))
			.Matches(subscription, descriptor, context.WithEvent(payload));
	}
}
