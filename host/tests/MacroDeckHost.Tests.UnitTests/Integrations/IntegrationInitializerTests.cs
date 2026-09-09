using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Infrastructure.Integrations;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Triggers;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using MacroDeck.Localization;

namespace MacroDeckHost.Tests.UnitTests.Integrations;

[TestFixture]
internal sealed class IntegrationInitializerTests
{
	[Test]
	public async Task A_hung_integration_does_not_delay_the_integrations_after_it()
	{
		// This must drive the real production fan-out (IntegrationStartupBackgroundService.InitializeAllAsync,
		// which is Task.WhenAll over the pending list) with the hung integration listed FIRST. Three
		// independent, directly-invoked InitializeAsync calls would pass even against a sequential
		// implementation, or against a hung integration whose block never reaches the enumerating thread, so
		// neither catches a regression here - only routing hung-first through the real fan-out, with hung
		// blocking its own calling thread the same way the sibling test does, does.
		using var serviceProvider = BuildScopeServices();
		var initializer = CreateInitializer(serviceProvider, new ManualTimeProvider());
		var service = new IntegrationStartupBackgroundService(new NoOpHostLifetime(),
			null!,
			serviceProvider.GetRequiredService<IServiceScopeFactory>(),
			new UserNotificationStore(),
			initializer,
			new RecordingMediator(),
			TimeProvider.System,
			new LoggerConfiguration().CreateLogger());

		using var gate = new ManualResetEventSlim(false);
		var hungThreadEntered = new TaskCompletionSource();
		var aStarted = new TaskCompletionSource();
		var bStarted = new TaskCompletionSource();

		var hung = new ScenarioIntegration
		{
			Id = "hung",
			BeforeReturningTask = () =>
			{
				hungThreadEntered.SetResult();
				gate.Wait();
			}
		};
		var a = new ScenarioIntegration
		{
			Id = "a", Work = () =>
			{
				aStarted.SetResult();
				return Task.CompletedTask;
			}
		};
		var b = new ScenarioIntegration
		{
			Id = "b", Work = () =>
			{
				bStarted.SetResult();
				return Task.CompletedTask;
			}
		};

		IntegrationStartupBackgroundService.PendingIntegration[] pending =
		[
			new(hung, "hung"),
			new(a, "a"),
			new(b, "b")
		];

		_ = service.InitializeAllAsync(pending, CancellationToken.None);

		await hungThreadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Task.WhenAll(aStarted.Task, bStarted.Task).WaitAsync(TimeSpan.FromSeconds(5));

		gate.Set();
	}

	[Test]
	public async Task An_integration_that_blocks_its_calling_thread_does_not_delay_the_others()
	{
		using var serviceProvider = BuildScopeServices();
		var initializer = CreateInitializer(serviceProvider, new ManualTimeProvider());

		using var gate = new ManualResetEventSlim(false);
		var hungThreadEntered = new TaskCompletionSource();
		var aStarted = new TaskCompletionSource();
		var bStarted = new TaskCompletionSource();

		var a = new ScenarioIntegration
		{
			Id = "a", Work = () =>
			{
				aStarted.SetResult();
				return Task.CompletedTask;
			}
		};
		var hung = new ScenarioIntegration
		{
			Id = "hung",
			BeforeReturningTask = () =>
			{
				hungThreadEntered.SetResult();
				gate.Wait();
			}
		};
		var b = new ScenarioIntegration
		{
			Id = "b", Work = () =>
			{
				bStarted.SetResult();
				return Task.CompletedTask;
			}
		};

		_ = initializer.InitializeAsync(a, "a");
		_ = initializer.InitializeAsync(hung, "hung");
		_ = initializer.InitializeAsync(b, "b");

		await hungThreadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Task.WhenAll(aStarted.Task, bStarted.Task).WaitAsync(TimeSpan.FromSeconds(5));

		gate.Set();
	}

	[Test]
	public async Task A_timed_out_integration_is_raised_as_an_issue_exactly_once_and_does_not_reconcile_later()
	{
		var timeProvider = new ManualTimeProvider();
		using var serviceProvider = BuildScopeServices();
		var hostIssueStore = new FakeIntegrationHostIssueStore();
		var initializer = CreateInitializer(serviceProvider, timeProvider, hostIssueStore);

		var hungGate = new TaskCompletionSource();
		var hungStarted = new TaskCompletionSource();
		var hung = new ScenarioIntegration
		{
			Id = "hung",
			Work = () =>
			{
				hungStarted.SetResult();
				return hungGate.Task;
			}
		};

		var outcomeTask = initializer.InitializeAsync(hung, "hung");
		await hungStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

		timeProvider.Advance(TimeSpan.FromSeconds(29));
		Assert.That(outcomeTask.IsCompleted,
			Is.False,
			"29 s must not be enough to time out a 30 s budget");

		timeProvider.Advance(TimeSpan.FromSeconds(2));
		var outcome = await outcomeTask.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(outcome, Is.EqualTo(IntegrationInitializationOutcome.TimedOut));
			Assert.That(hostIssueStore.Has("hung"), Is.True);
			var issues = hostIssueStore.IssuesFor("hung");
			Assert.That(issues, Has.Count.EqualTo(1));
			Assert.That(issues[0].Id, Is.EqualTo(IntegrationHostIssueIds.Startup));
			Assert.That(issues[0].Severity, Is.EqualTo(MacroDeck.Sdk.Issues.IntegrationIssueSeverity.Error));
			Assert.That(hostIssueStore.RefreshRequests, Is.EqualTo(1));
		});

		hungGate.SetResult();
		timeProvider.Advance(TimeSpan.FromSeconds(60));

		Assert.Multiple(() =>
		{
			Assert.That(hostIssueStore.Has("hung"),
				Is.True,
				"an abandoned task completing later must not reconcile the already-raised timeout issue");
			Assert.That(hostIssueStore.RefreshRequests, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task A_throwing_integration_is_isolated_and_the_rest_still_initialize()
	{
		using var serviceProvider = BuildScopeServices();
		var hostIssueStore = new FakeIntegrationHostIssueStore();
		var initializer = CreateInitializer(serviceProvider, new ManualTimeProvider(), hostIssueStore);

		var thrower = new ScenarioIntegration
		{
			Id = "thrower",
			BeforeReturningTask = () => throw new InvalidOperationException("could not connect")
		};
		var healthy = new ScenarioIntegration { Id = "healthy" };

		var throwerOutcome = await initializer.InitializeAsync(thrower, "thrower");
		var healthyOutcome = await initializer.InitializeAsync(healthy, "healthy");

		Assert.Multiple(() =>
		{
			Assert.That(throwerOutcome, Is.EqualTo(IntegrationInitializationOutcome.Failed));
			Assert.That(healthyOutcome, Is.EqualTo(IntegrationInitializationOutcome.Initialized));
			Assert.That(hostIssueStore.Has("thrower"), Is.True);
			Assert.That(hostIssueStore.Has("healthy"), Is.False);
		});
	}

	[Test]
	public async Task Host_shutdown_during_startup_does_not_report_failures()
	{
		var timeProvider = new ManualTimeProvider();
		using var serviceProvider = BuildScopeServices();
		var hostIssueStore = new FakeIntegrationHostIssueStore();
		var initializer = CreateInitializer(serviceProvider, timeProvider, hostIssueStore);

		using var cts = new CancellationTokenSource();
		var started = new TaskCompletionSource();
		var integration = new ScenarioIntegration
		{
			Id = "slow",
			Work = () =>
			{
				started.SetResult();
				return new TaskCompletionSource().Task;
			}
		};

		var outcomeTask = initializer.InitializeAsync(integration, "slow", cts.Token);
		await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

		cts.Cancel();
		var outcome = await outcomeTask.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(outcome, Is.EqualTo(IntegrationInitializationOutcome.Aborted));
			Assert.That(hostIssueStore.Has("slow"), Is.False);
			Assert.That(hostIssueStore.RefreshRequests, Is.Zero);
		});

		timeProvider.Advance(TimeSpan.FromSeconds(60));

		Assert.Multiple(() =>
		{
			Assert.That(hostIssueStore.Has("slow"),
				Is.False,
				"cancellation must disarm the timeout, not merely race it");
			Assert.That(hostIssueStore.RefreshRequests, Is.Zero);
		});
	}

	private sealed class NoOpHostLifetime : IHostApplicationLifetime
	{
		public CancellationToken ApplicationStarted => CancellationToken.None;
		public CancellationToken ApplicationStopping => CancellationToken.None;
		public CancellationToken ApplicationStopped => CancellationToken.None;

		public void StopApplication()
		{
		}
	}

	private static IntegrationInitializer CreateInitializer(
		ServiceProvider serviceProvider,
		TimeProvider timeProvider,
		IIntegrationHostIssueStore? hostIssueStore = null)
		=> new(serviceProvider.GetRequiredService<IServiceScopeFactory>(),
			new FakeDeckNavigator(),
			new FakeScriptApi(),
			new FakeWidgetApi(),
			new FakeWidgetIconInvalidator(),
			new FakeUserVariableApi(),
			new RecordingEventBus(),
			new UserNotificationStore(),
			null!,
			null!,
			new VariableRefreshSignal(),
			hostIssueStore ?? new FakeIntegrationHostIssueStore(),
			TestLayoutProviders.Host(),
			TestFolderViewProviders.Host(),
			TestWidgetTypeProviders.Host(),
			TestDeviceProviders.Host(),
			timeProvider,
			new LoggerConfiguration().CreateLogger());

	private static ServiceProvider BuildScopeServices()
		=> new ServiceCollection()
			.AddSingleton<IVariableService>(new ThrowingVariableService())
			.AddSingleton(TestLocalization.Resolver)
			.AddSingleton(TestLocalization.Preferences)
			.BuildServiceProvider();

	private sealed class ScenarioIntegration : IIntegration
	{
		public string Id { get; init; } = "test";
		public LocalizedText Name => Id;
		public string Version => "1.0.0";
		public IReadOnlyList<IActionDefinition> Actions => [];
		public bool IsInitialized { get; private set; }
		public Func<Task>? Work { get; init; }
		public Action? BeforeReturningTask { get; init; }

		public Task InitializeAsync(IIntegrationContext context)
		{
			BeforeReturningTask?.Invoke();
			var task = Work?.Invoke() ?? Task.CompletedTask;
			IsInitialized = true;
			return task;
		}

		public Task ShutdownAsync() => Task.CompletedTask;
	}
}
