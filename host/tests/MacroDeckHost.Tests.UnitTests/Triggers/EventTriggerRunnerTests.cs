using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Triggers;

[TestFixture]
public class EventTriggerRunnerTests
{
	private const string Flows = """[{"triggerId":"t1","triggerType":"onEvent","children":[]}]""";

	private StubFolderCache _folders = null!;
	private StubAutomationCache _automations = null!;
	private RecordingFlowExecutor _executor = null!;
	private EventTriggerRunner _runner = null!;

	[SetUp]
	public void SetUp()
	{
		_folders = new StubFolderCache();
		_automations = new StubAutomationCache();
		_executor = new RecordingFlowExecutor();
		_runner = new EventTriggerRunner(_folders,
			_automations,
			new EmptyEventRegistry(),
			new AlwaysMatchingMatcher(),
			new EmptyVariableRenderer(),
			_executor,
			new LoggerConfiguration().CreateLogger());
	}

	private static EventSubscription Subscription(EventTriggerOwner owner)
		=> new(owner, "t1", "obs::scene-changed", EventSubscription.NoConfiguration, null);

	private static EventOccurrence Occurrence(EventSubscription subscription)
		=> new(subscription.EventId, EventOccurrence.NoParameters, subscription.Target);

	[Test]
	public async Task A_widget_trigger_runs_its_widget_data_in_action_button_scope()
	{
		var widget = StubFolderCache.Widget(WidgetFlowsJson.ToSource(Flows));
		_folders.AddFolder(widget);
		var subscription = Subscription(EventTriggerOwner.ForWidget(widget.Id));

		await _runner.Run(subscription, Occurrence(subscription), CancellationToken.None);

		var request = _executor.Requests.Single();
		Assert.Multiple(() =>
		{
			Assert.That(request.FlowsSource, Is.EqualTo(widget.Data));
			Assert.That(request.Scope, Is.EqualTo(VariableScope.Widget));
			Assert.That(request.ScopeRefId, Is.EqualTo(widget.Id.ToString()));
			Assert.That(request.Trigger.ByTriggerId, Is.True);
			Assert.That(request.Trigger.Value, Is.EqualTo("t1"));
		});
	}

	[Test]
	public async Task A_widget_trigger_marks_its_request_host_originated_so_it_is_not_gated_while_locked()
	{
		var widget = StubFolderCache.Widget(WidgetFlowsJson.ToSource(Flows));
		_folders.AddFolder(widget);
		var subscription = Subscription(EventTriggerOwner.ForWidget(widget.Id));

		await _runner.Run(subscription, Occurrence(subscription), CancellationToken.None);

		Assert.That(_executor.Requests.Single().Origin, Is.EqualTo(ExecutionOrigin.Host));
	}

	[Test]
	public async Task A_widget_trigger_carries_its_widget_as_the_flow_owner()
	{
		var widget = StubFolderCache.Widget(WidgetFlowsJson.ToSource(Flows));
		_folders.AddFolder(widget);
		var subscription = Subscription(EventTriggerOwner.ForWidget(widget.Id));

		await _runner.Run(subscription, Occurrence(subscription), CancellationToken.None);

		Assert.That(_executor.Requests.Single().OwnerWidgetId, Is.EqualTo(widget.Id));
	}

	[Test]
	public async Task An_automation_trigger_runs_its_flows_in_global_scope()
	{
		var automation = _automations.Add(Flows);
		var subscription = Subscription(EventTriggerOwner.ForAutomation(automation.Id));

		await _runner.Run(subscription, Occurrence(subscription), CancellationToken.None);

		var request = _executor.Requests.Single();
		Assert.Multiple(() =>
		{
			Assert.That(WidgetFlowsJson.TryExtract(request.FlowsSource, out var extracted), Is.True);
			Assert.That(extracted, Is.EqualTo(Flows));
			Assert.That(request.Scope, Is.EqualTo(VariableScope.Global));
			Assert.That(request.ScopeRefId, Is.Null);
			Assert.That(request.OwnerWidgetId, Is.Null);
			Assert.That(request.Trigger.ByTriggerId, Is.True);
			Assert.That(request.Trigger.Value, Is.EqualTo("t1"));
		});
	}

	[Test]
	public async Task A_deleted_widget_is_a_no_op()
	{
		var subscription = Subscription(EventTriggerOwner.ForWidget(Guid.NewGuid()));

		await _runner.Run(subscription, Occurrence(subscription), CancellationToken.None);

		Assert.That(_executor.Requests, Is.Empty);
	}

	[Test]
	public async Task A_deleted_automation_is_a_no_op()
	{
		var subscription = Subscription(EventTriggerOwner.ForAutomation(Guid.NewGuid()));

		await _runner.Run(subscription, Occurrence(subscription), CancellationToken.None);

		Assert.That(_executor.Requests, Is.Empty);
	}

	[Test]
	public async Task A_disabled_automation_does_not_run_even_if_an_occurrence_is_already_queued()
	{
		var automation = _automations.Add(Flows, enabled: false);
		var subscription = Subscription(EventTriggerOwner.ForAutomation(automation.Id));

		await _runner.Run(subscription, Occurrence(subscription), CancellationToken.None);

		Assert.That(_executor.Requests, Is.Empty);
	}

	[Test]
	public async Task The_occurrences_parameters_are_handed_to_the_executor()
	{
		var automation = _automations.Add(Flows);
		var subscription = Subscription(EventTriggerOwner.ForAutomation(automation.Id));
		var parameters = new Dictionary<string, object?>(StringComparer.Ordinal) { ["sceneName"] = "Live" };
		var occurrence = new EventOccurrence(subscription.EventId, parameters, subscription.Target);

		await _runner.Run(subscription, occurrence, CancellationToken.None);

		Assert.That(_executor.Requests.Single().EventParameters, Is.EqualTo(parameters));
	}

	[Test]
	public async Task An_untargeted_occurrence_for_an_unknown_event_does_not_run()
	{
		var automation = _automations.Add(Flows);
		var subscription = Subscription(EventTriggerOwner.ForAutomation(automation.Id));
		var occurrence = new EventOccurrence(subscription.EventId, EventOccurrence.NoParameters);

		await _runner.Run(subscription, occurrence, CancellationToken.None);

		Assert.That(_executor.Requests, Is.Empty);
	}

	private sealed class RecordingFlowExecutor : IFlowExecutor
	{
		public List<FlowExecutionRequest> Requests { get; } = [];

		public Task<FlowExecutionResult> ExecuteAsync(FlowExecutionRequest request, CancellationToken cancellationToken)
		{
			Requests.Add(request);
			return Task.FromResult(new FlowExecutionResult
			{
				ExecutionId = Guid.NewGuid(),
				Status = FlowExecutionStatus.Succeeded,
				MatchedFlows = 1
			});
		}
	}

	private sealed class EmptyEventRegistry : IEventRegistry
	{
		public IReadOnlyList<EventDefinitionDescriptor> GetDefinitions() => [];

		public EventDefinitionDescriptor? Find(string qualifiedEventId) => null;

		public object? FindProvider(string qualifiedEventId) => null;
	}

	private sealed class AlwaysMatchingMatcher : IEventSubscriptionMatcher
	{
		public bool Matches(
			EventSubscription subscription,
			EventDefinitionDescriptor descriptor,
			VariableContext context)
			=> true;
	}

	private sealed class EmptyVariableRenderer : IVariableTemplateRenderer
	{
		public Task<string> RenderAsync(string templateText, VariableScope contextScope, string? contextScopeRefId)
			=> Task.FromResult(templateText);

		public Task<VariableContext> CreateContextAsync(VariableScope contextScope, string? contextScopeRefId)
			=> Task.FromResult(VariableContext.Empty);

		public string Render(string templateText, VariableContext context) => templateText;
	}
}
