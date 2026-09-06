using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Ui.Transport.Messages.Integrations;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.Issues;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using MacroDeck.Localization;

namespace MacroDeckHost.Tests.UnitTests.Integrations;

[TestFixture]
internal sealed class IntegrationIssueBroadcastBackgroundServiceTests
{
	[Test]
	public async Task Tick_sends_one_event_with_count_and_max_severity_when_issues_first_appear()
	{
		var registry = new FakeIssueRegistry();
		registry.Add("app.test.one", enabled: true);
		var issueService = new FakeIssueService();
		issueService.Set("app.test.one",
			Issue("a", IntegrationIssueSeverity.Warning),
			Issue("b", IntegrationIssueSeverity.Error));
		var transport = new RecordingUiTransport();
		var service = CreateService(registry, issueService, transport);

		await service.Tick(CancellationToken.None);

		var events = Events(transport);
		Assert.Multiple(() =>
		{
			Assert.That(events, Has.Count.EqualTo(1));
			Assert.That(events[0].IntegrationId, Is.EqualTo("app.test.one"));
			Assert.That(events[0].IssueCount, Is.EqualTo(2));
			Assert.That(events[0].Severity, Is.EqualTo("error"));
		});
	}

	[Test]
	public async Task Tick_sends_nothing_when_the_issue_list_is_unchanged()
	{
		var registry = new FakeIssueRegistry();
		registry.Add("app.test.one", enabled: true);
		var issueService = new FakeIssueService();
		issueService.Set("app.test.one", Issue("a", IntegrationIssueSeverity.Warning));
		var transport = new RecordingUiTransport();
		var service = CreateService(registry, issueService, transport);

		await service.Tick(CancellationToken.None);
		await service.Tick(CancellationToken.None);

		Assert.That(Events(transport), Has.Count.EqualTo(1), "an identical issue list must not be re-sent");
	}

	[Test]
	public async Task Tick_sends_again_when_the_issues_change()
	{
		var registry = new FakeIssueRegistry();
		registry.Add("app.test.one", enabled: true);
		var issueService = new FakeIssueService();
		issueService.Set("app.test.one", Issue("a", IntegrationIssueSeverity.Warning));
		var transport = new RecordingUiTransport();
		var service = CreateService(registry, issueService, transport);

		await service.Tick(CancellationToken.None);
		issueService.Set("app.test.one",
			Issue("a", IntegrationIssueSeverity.Warning),
			Issue("b", IntegrationIssueSeverity.Error));
		await service.Tick(CancellationToken.None);

		var events = Events(transport);
		Assert.Multiple(() =>
		{
			Assert.That(events, Has.Count.EqualTo(2));
			Assert.That(events[^1].IssueCount, Is.EqualTo(2));
			Assert.That(events[^1].Severity, Is.EqualTo("error"));
		});
	}

	[Test]
	public async Task Tick_sends_exactly_one_final_empty_event_when_issues_resolve()
	{
		var registry = new FakeIssueRegistry();
		registry.Add("app.test.one", enabled: true);
		var issueService = new FakeIssueService();
		issueService.Set("app.test.one", Issue("a", IntegrationIssueSeverity.Warning));
		var transport = new RecordingUiTransport();
		var service = CreateService(registry, issueService, transport);

		await service.Tick(CancellationToken.None); // establishes the issue
		issueService.Set("app.test.one"); // resolved
		await service.Tick(CancellationToken.None); // final empty event
		await service.Tick(CancellationToken.None); // nothing more - the integration was forgotten

		var events = Events(transport);
		Assert.Multiple(() =>
		{
			Assert.That(events, Has.Count.EqualTo(2), "exactly one final empty event, then silence");
			Assert.That(events[^1].IssueCount, Is.EqualTo(0));
			Assert.That(events[^1].Severity, Is.Null);
			Assert.That(events[^1].Issues, Is.Empty);
		});
	}

	[Test]
	public async Task Tick_sends_a_final_empty_event_when_a_reporting_integration_is_disabled()
	{
		var registry = new FakeIssueRegistry();
		registry.Add("app.test.one", enabled: true);
		var issueService = new FakeIssueService();
		issueService.Set("app.test.one", Issue("a", IntegrationIssueSeverity.Warning));
		var transport = new RecordingUiTransport();
		var service = CreateService(registry, issueService, transport);

		await service.Tick(CancellationToken.None);
		registry.SetEnabledForTest("app.test.one", false);
		await service.Tick(CancellationToken.None);

		var events = Events(transport);
		Assert.That(events, Has.Count.EqualTo(2));
		Assert.That(events[^1].IssueCount, Is.EqualTo(0));
	}

	[Test]
	public void An_integration_with_no_issues_and_never_tracked_never_sends()
	{
		var registry = new FakeIssueRegistry();
		registry.Add("app.test.clean", enabled: true);
		var issueService = new FakeIssueService();
		var transport = new RecordingUiTransport();
		var service = CreateService(registry, issueService, transport);

		Assert.DoesNotThrowAsync(async () =>
		{
			await service.Tick(CancellationToken.None);
			await service.Tick(CancellationToken.None);
		});
		Assert.That(Events(transport), Is.Empty, "a permanently clean integration never needs an announcement");
	}

	[Test]
	public async Task Tick_raises_a_notification_once_for_a_new_error_issue_but_not_for_warning_or_info()
	{
		var registry = new FakeIssueRegistry();
		registry.Add("app.test.one", enabled: true);
		var issueService = new FakeIssueService();
		issueService.Set("app.test.one",
			Issue("info-issue", IntegrationIssueSeverity.Info),
			Issue("warning-issue", IntegrationIssueSeverity.Warning),
			Issue("error-issue", IntegrationIssueSeverity.Error));
		var store = new UserNotificationStore();
		var service = CreateService(registry, issueService, new RecordingUiTransport(), store);

		await service.Tick(CancellationToken.None);
		var firstSequence = store.Snapshot().Single().Sequence;
		await service.Tick(CancellationToken.None); // the issue persists - must not raise again

		var notifications = store.Snapshot();
		Assert.Multiple(() =>
		{
			Assert.That(notifications, Has.Count.EqualTo(1), "only the Error-severity issue raises");
			Assert.That(notifications[0].SourceId, Is.EqualTo("app.test.one"));
			Assert.That(notifications[0].Severity, Is.EqualTo(UserNotificationSeverity.Error));
			Assert.That(notifications[0].Sequence,
				Is.EqualTo(firstSequence),
				"a persisting issue must not be re-raised on every poll");
		});
	}

	[Test]
	public async Task Tick_raises_again_when_an_error_issue_resolves_and_recurs()
	{
		var registry = new FakeIssueRegistry();
		registry.Add("app.test.one", enabled: true);
		var issueService = new FakeIssueService();
		issueService.Set("app.test.one", Issue("error-issue", IntegrationIssueSeverity.Error));
		var store = new UserNotificationStore();
		var service = CreateService(registry, issueService, new RecordingUiTransport(), store);

		await service.Tick(CancellationToken.None);
		var firstSequence = store.Snapshot().Single().Sequence;

		issueService.Set("app.test.one"); // resolved
		await service.Tick(CancellationToken.None);
		issueService.Set("app.test.one", Issue("error-issue", IntegrationIssueSeverity.Error)); // recurs
		await service.Tick(CancellationToken.None);

		var notifications = store.Snapshot();
		Assert.Multiple(() =>
		{
			Assert.That(notifications, Has.Count.EqualTo(1), "the recurrence replaces via the dedupe key");
			Assert.That(notifications[0].Sequence,
				Is.GreaterThan(firstSequence),
				"a resolved-then-recurring issue must be announced again");
		});
	}

	[Test]
	public async Task Tick_notifies_for_a_host_issue_on_an_integration_that_is_not_an_issue_provider()
	{
		var registry = new FakeIssueRegistry();
		registry.AddPlain("app.test.plain", enabled: true);
		var issueService = new FakeIssueService();
		issueService.Set("app.test.plain", Issue(IntegrationHostIssueIds.Startup, IntegrationIssueSeverity.Error));
		var store = new CountingUserNotificationStore();
		var service = CreateService(registry, issueService, new RecordingUiTransport(), store);

		await service.Tick(CancellationToken.None);
		await service.Tick(CancellationToken.None); // the issue persists - must not raise again
		await service.Tick(CancellationToken.None); // still persists - still must not raise again

		// UserNotificationStore.Raise dedupes by replacing in place, so Snapshot().Count == 1 cannot tell
		// "raised once" from "raised on every tick and replaced each time" - CountingUserNotificationStore
		// counts the calls themselves instead.
		Assert.That(store.CallCount("integration-issue:app.test.plain:host.startup"),
			Is.EqualTo(1),
			"a non-provider integration's host issue still notifies exactly once, now that Tick no longer " +
			"filters on IIntegrationIssueProvider");
	}

	private static IntegrationIssue Issue(string id, IntegrationIssueSeverity severity)
		=> new() { Id = id, Title = id, Severity = severity };

	private static List<IntegrationIssuesChangedEvent> Events(RecordingUiTransport transport)
		=> transport.Broadcasts.OfType<IntegrationIssuesChangedEvent>().ToList();

	private static IntegrationIssueBroadcastBackgroundService CreateService(
		IIntegrationRegistry registry,
		IIntegrationIssueService issueService,
		RecordingUiTransport transport,
		IUserNotificationStore? userNotificationStore = null)
		=> new(new StartedHostLifetime(),
			registry,
			issueService,
			new NoOpBroadcastTrigger(),
			transport,
			userNotificationStore ?? new UserNotificationStore(),
			TestLocalization.ScopeFactory,
			SilentLogger());

	private static Logger SilentLogger() => new LoggerConfiguration().CreateLogger();

	private sealed class FakeIssueService : IIntegrationIssueService
	{
		private readonly Dictionary<string, IReadOnlyList<IntegrationIssue>> _issues = new(StringComparer.Ordinal);

		public void Set(string integrationId, params IntegrationIssue[] issues) => _issues[integrationId] = issues;

		public Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(
			string integrationId,
			CancellationToken cancellationToken = default)
			=> Task.FromResult(_issues.GetValueOrDefault(integrationId, []));

		public Task<IssueResolution?> ResolveAsync(
			string integrationId,
			string issueId,
			CancellationToken cancellationToken = default)
			=> throw new NotSupportedException("Not exercised by the broadcast service.");
	}

	private sealed class FakeIssueRegistry : IIntegrationRegistry
	{
		public event EventHandler<IntegrationAvailabilityChangedEventArgs>? AvailabilityChanged
		{
			add { }
			remove { }
		}

		private readonly List<IIntegration> _integrations = [];
		private readonly HashSet<string> _enabled = new(StringComparer.Ordinal);

		public IReadOnlyList<IIntegration> Integrations => _integrations;

		public void Add(string id, bool enabled)
		{
			_integrations.Add(new FakeIssueIntegration(id));
			SetEnabledForTest(id, enabled);
		}

		public void AddPlain(string id, bool enabled)
		{
			_integrations.Add(new FakePlainIntegration(id));
			SetEnabledForTest(id, enabled);
		}

		public void SetEnabledForTest(string id, bool enabled)
		{
			if (enabled)
			{
				_enabled.Add(id);
			}
			else
			{
				_enabled.Remove(id);
			}
		}

		public IActionDefinition? FindAction(string integrationId, string actionId) => null;

		public IActionDefinition? FindAction(QualifiedId id) => null;

		public IReadOnlyList<ActionDescriptor> GetActions(bool enabledOnly = true) => [];

		public bool IsEnabled(string integrationId) => _enabled.Contains(integrationId);

		public void SetEnabled(string integrationId, bool enabled) => SetEnabledForTest(integrationId, enabled);

		public IntegrationOrigin GetOrigin(string integrationId) => IntegrationOrigin.BuiltIn;

		public Task<IntegrationRegistrationResult> RegisterAsync(
			IIntegration integration,
			IntegrationOrigin origin = IntegrationOrigin.BuiltIn,
			IntegrationMetadata? metadata = null)
			=> Task.FromResult(IntegrationRegistrationResult.Success);

		public Task<bool> UnregisterAsync(string integrationId) => Task.FromResult(false);
	}

	private sealed class FakeIssueIntegration : IIntegration, IIntegrationIssueProvider
	{
		public FakeIssueIntegration(string id)
		{
			Id = id;
		}

		public string Id { get; }
		public LocalizedText Name => Id;
		public string Version => "1.0.0";
		public IReadOnlyList<IActionDefinition> Actions => [];
		public bool IsInitialized => true;

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;

		public Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();
	}

	private sealed class FakePlainIntegration : IIntegration
	{
		public FakePlainIntegration(string id)
		{
			Id = id;
		}

		public string Id { get; }
		public LocalizedText Name => Id;
		public string Version => "1.0.0";
		public IReadOnlyList<IActionDefinition> Actions => [];
		public bool IsInitialized => true;

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;
	}

	private sealed class NoOpBroadcastTrigger : IIntegrationIssueBroadcastTrigger
	{
		public void RequestRefresh()
		{
		}

		public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
			=> Task.FromResult(false);
	}

	private sealed class StartedHostLifetime : IHostApplicationLifetime
	{
		public CancellationToken ApplicationStarted { get; } = new(true);
		public CancellationToken ApplicationStopping => CancellationToken.None;
		public CancellationToken ApplicationStopped => CancellationToken.None;

		public void StopApplication()
		{
		}
	}
}
