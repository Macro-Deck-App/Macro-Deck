using System.Globalization;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Infrastructure.Integrations;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Triggers;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using DomainVariableType = MacroDeckHost.Domain.Enums.VariableType;
using MacroDeck.Localization;

namespace MacroDeckHost.Tests.UnitTests.Integrations;

[TestFixture]
internal sealed class IntegrationStartupBackgroundServiceTests
{
	private static readonly string[] _expectedRegistrationOrder = ["early", "later"];

	[Test]
	public async Task Host_stop_awaits_initialized_integration_shutdown()
	{
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var integration = new RecordingIntegration
		{
			ShutdownWork = () =>
			{
				started.TrySetResult();
				return release.Task;
			}
		};
		var service = new IntegrationStartupBackgroundService(new LifetimeStub(),
			new FixedRegistry(integration),
			null!,
			new UserNotificationStore(),
			EmptyInitializer(),
			new RecordingMediator(),
			TimeProvider.System,
			new LoggerConfiguration().CreateLogger());
		var stopping = service.StopAsync(CancellationToken.None);

		await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.That(integration.ShutdownCalls, Is.EqualTo(1));
		Assert.That(stopping.IsCompleted,
			Is.False,
			"host stop must wait for a provider's pending durable writes");

		release.SetResult();
		await stopping.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.That(integration.ShutdownCalls, Is.EqualTo(1));
	}

	[Test]
	public async Task Host_stop_skips_uninitialized_integrations_and_isolates_shutdown_failures()
	{
		var failing = new RecordingIntegration
		{
			ShutdownWork = () => throw new IOException("provider failed")
		};
		var healthy = new RecordingIntegration();
		var neverAttempted = new RecordingIntegration { Id = "test.never-attempted", IsInitialized = false };
		var alreadyDisabled = new RecordingIntegration { Id = "test.disabled" };

		await IntegrationStartupBackgroundService.ShutdownIntegrationsAsync(
			new FixedRegistry([failing, healthy, neverAttempted, alreadyDisabled], [alreadyDisabled.Id]),
			new LoggerConfiguration().CreateLogger(),
			new HashSet<string>(StringComparer.Ordinal),
			TimeProvider.System);

		Assert.Multiple(() =>
		{
			Assert.That(failing.ShutdownCalls, Is.EqualTo(1));
			Assert.That(healthy.ShutdownCalls, Is.EqualTo(1));
			Assert.That(neverAttempted.ShutdownCalls, Is.Zero);
			Assert.That(alreadyDisabled.ShutdownCalls,
				Is.Zero,
				"disable already performed the provider shutdown");
		});
	}

	[Test]
	public async Task An_integration_whose_initialization_timed_out_is_still_shut_down()
	{
		var timedOut = new RecordingIntegration { Id = "test.timed-out", IsInitialized = false };
		var neverAttempted = new RecordingIntegration { Id = "test.never-attempted", IsInitialized = false };
		var disabled = new RecordingIntegration { Id = "test.disabled", IsInitialized = false };

		await IntegrationStartupBackgroundService.ShutdownIntegrationsAsync(
			new FixedRegistry([timedOut, neverAttempted, disabled], [disabled.Id]),
			new LoggerConfiguration().CreateLogger(),
			new HashSet<string>(StringComparer.Ordinal) { timedOut.Id },
			TimeProvider.System);

		Assert.Multiple(() =>
		{
			Assert.That(timedOut.ShutdownCalls,
				Is.EqualTo(1),
				"IsInitialized stays false when initialization timed out, but it was still attempted");
			Assert.That(neverAttempted.ShutdownCalls, Is.Zero);
			Assert.That(disabled.ShutdownCalls, Is.Zero);
		});
	}

	[Test]
	public async Task One_stuck_shutdown_does_not_block_the_other_shutdowns()
	{
		var timeProvider = new ManualTimeProvider();
		var stuckStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var stuck = new RecordingIntegration
		{
			Id = "test.stuck",
			ShutdownWork = () =>
			{
				stuckStarted.TrySetResult();
				return new TaskCompletionSource().Task;
			}
		};
		var healthyStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var healthy = new RecordingIntegration
		{
			Id = "test.healthy",
			ShutdownWork = () =>
			{
				healthyStarted.TrySetResult();
				return Task.CompletedTask;
			}
		};
		var events = new List<LogEvent>();
		var logger = new LoggerConfiguration().WriteTo.Sink(new CapturingSink(events)).CreateLogger();

		var shutdown = IntegrationStartupBackgroundService.ShutdownIntegrationsAsync(
			new FixedRegistry([stuck, healthy], []),
			logger,
			new HashSet<string>(StringComparer.Ordinal),
			timeProvider);

		// Both shutdowns have to have actually started before the clock moves. Each one is wrapped in a
		// Task.Run whose timeout runs on this same virtual clock, so advancing while the healthy one is
		// still waiting for a pool thread would time *it* out too - and the run it never got would look
		// exactly like the blocking this test is here to rule out.
		await Task.WhenAll(stuckStarted.Task, healthyStarted.Task).WaitAsync(TimeSpan.FromSeconds(5));
		Assert.That(shutdown.IsCompleted, Is.False);

		timeProvider.Advance(TimeSpan.FromSeconds(10));
		await shutdown.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(healthy.ShutdownCalls, Is.EqualTo(1), "the stuck shutdown must not block the healthy one");
			Assert.That(events.Any(e =>
					e.RenderMessage(CultureInfo.InvariantCulture).Contains(stuck.Id, StringComparison.Ordinal)),
				Is.True,
				"the stuck shutdown must be logged");
		});
	}

	[Test]
	public async Task Registration_order_follows_discovery_order_even_though_initialization_is_parallel()
	{
		var registry = new ThreadRecordingIntegrationRegistry();
		using var serviceProvider = BuildScopeServices();
		var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
		var initializer = CreateRealInitializer(scopeFactory);
		var service = new IntegrationStartupBackgroundService(new LifetimeStub(),
			registry,
			scopeFactory,
			new UserNotificationStore(),
			initializer,
			new RecordingMediator(),
			TimeProvider.System,
			new LoggerConfiguration().CreateLogger());

		var earlyStarted = new TaskCompletionSource();
		var laterStarted = new TaskCompletionSource();
		var early = new RecordingIntegration
		{
			Id = "early",
			OnInitializeStart = () => registry.RecordEvent("initialize:early"),
			InitializeWork = async () =>
			{
				earlyStarted.SetResult();
				await laterStarted.Task;
			}
		};
		var later = new RecordingIntegration
		{
			Id = "later",
			OnInitializeStart = () => registry.RecordEvent("initialize:later"),
			InitializeWork = () =>
			{
				laterStarted.SetResult();
				return Task.CompletedTask;
			}
		};

		var pending = await service.RegisterAllAsync([early, later]);
		await service.InitializeAllAsync(pending, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

		var lastRegisterIndex = registry.Events.FindLastIndex(e => e.StartsWith("register:", StringComparison.Ordinal));
		var firstInitializeIndex
			= registry.Events.FindIndex(e => e.StartsWith("initialize:", StringComparison.Ordinal));

		Assert.Multiple(() =>
		{
			Assert.That(registry.RegisteredIds, Is.EqualTo(_expectedRegistrationOrder));
			Assert.That(firstInitializeIndex, Is.GreaterThanOrEqualTo(0), "initialization must have begun");
			Assert.That(lastRegisterIndex,
				Is.LessThan(firstInitializeIndex),
				"no RegisterAsync call may begin after any InitializeAsync has begun");
		});
	}

	private static IntegrationInitializer EmptyInitializer()
		=> new(null!,
			null!,
			null!,
			null!,
			null!,
			null!,
			null!,
			null!,
			null!,
			null!,
			null!,
			new VariableRefreshSignal(),
			new FakeIntegrationHostIssueStore(),
			TestLayoutProviders.Host(),
			TestFolderViewProviders.Host(),
			TestWidgetTypeProviders.Host(),
			TestDeviceProviders.Host(),
			TimeProvider.System,
			new LoggerConfiguration().CreateLogger());

	private static IntegrationInitializer CreateRealInitializer(IServiceScopeFactory scopeFactory)
		=> new(scopeFactory,
			new NoOpDeckNavigator(),
			null!,
			null!,
			new FakeWidgetIconInvalidator(),
			null!,
			new RecordingEventBus(),
			new UserNotificationStore(),
			null!,
			null!,
			null!,
			new VariableRefreshSignal(),
			new FakeIntegrationHostIssueStore(),
			TestLayoutProviders.Host(),
			TestFolderViewProviders.Host(),
			TestWidgetTypeProviders.Host(),
			TestDeviceProviders.Host(),
			TimeProvider.System,
			new LoggerConfiguration().CreateLogger());

	private static ServiceProvider BuildScopeServices()
		=> new ServiceCollection()
			.AddSingleton<IVariableService>(new NotSupportedVariableService())
			.AddSingleton(TestLocalization.Resolver)
			.AddSingleton(TestLocalization.Preferences)
			.BuildServiceProvider();

	private sealed class CapturingSink : ILogEventSink
	{
		private readonly List<LogEvent> _events;

		public CapturingSink(List<LogEvent> events) => _events = events;

		public void Emit(LogEvent logEvent) => _events.Add(logEvent);
	}

	private sealed class NoOpDeckNavigator : MacroDeck.Sdk.Decks.IDeckNavigator
	{
		public Task ChangeFolderAsync(string folderId,
			string? originClientId = null,
			CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task ChangeProfileAsync(string profileId,
			string? originClientId = null,
			CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task GoToParentAsync(string? originClientId = null, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task GoBackAsync(string? originClientId = null, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public IReadOnlyList<MacroDeck.Sdk.Decks.DeckFolder> GetFolders() => [];

		public IReadOnlyList<MacroDeck.Sdk.Decks.DeckProfile> GetProfiles() => [];
	}

	private sealed class FixedRegistry : IIntegrationRegistry
	{
		public event EventHandler<IntegrationAvailabilityChangedEventArgs>? AvailabilityChanged
		{
			add { }
			remove { }
		}

		private readonly HashSet<string> _disabled;

		public FixedRegistry(params IIntegration[] integrations)
			: this(integrations, [])
		{
		}

		public FixedRegistry(IIntegration[] integrations, string[] disabled)
		{
			Integrations = integrations;
			_disabled = [.. disabled];
		}

		public IReadOnlyList<IIntegration> Integrations { get; }
		public IActionDefinition? FindAction(string integrationId, string actionId) => null;
		public IActionDefinition? FindAction(QualifiedId id) => null;
		public IReadOnlyList<ActionDescriptor> GetActions(bool enabledOnly = true) => [];
		public bool IsEnabled(string integrationId) => !_disabled.Contains(integrationId);

		public void SetEnabled(string integrationId, bool enabled)
		{
		}

		public IntegrationOrigin GetOrigin(string integrationId) => IntegrationOrigin.BuiltIn;

		public Task<IntegrationRegistrationResult> RegisterAsync(
			IIntegration integration,
			IntegrationOrigin origin = IntegrationOrigin.BuiltIn,
			IntegrationMetadata? metadata = null)
			=> Task.FromResult(IntegrationRegistrationResult.Success);

		public Task<bool> UnregisterAsync(string integrationId) => Task.FromResult(false);
	}

	private sealed class LifetimeStub : IHostApplicationLifetime
	{
		public CancellationToken ApplicationStarted => CancellationToken.None;
		public CancellationToken ApplicationStopping => CancellationToken.None;
		public CancellationToken ApplicationStopped => CancellationToken.None;

		public void StopApplication()
		{
		}
	}

	private sealed class RecordingIntegration : IIntegration
	{
		public string Id { get; init; } = "test.integration";
		public LocalizedText Name => "Test";
		public string Version => "1.0.0";
		public bool IsInitialized { get; init; } = true;
		public IReadOnlyList<IActionDefinition> Actions => [];
		public int ShutdownCalls { get; private set; }
		public Func<Task>? ShutdownWork { get; init; }
		public Func<Task>? InitializeWork { get; init; }
		public Action? OnInitializeStart { get; init; }

		public Task InitializeAsync(IIntegrationContext context)
		{
			OnInitializeStart?.Invoke();
			return InitializeWork?.Invoke() ?? Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			ShutdownCalls++;
			return ShutdownWork?.Invoke() ?? Task.CompletedTask;
		}
	}

	private sealed class NotSupportedVariableService : IVariableService
	{
		public Task<IReadOnlyList<VariableEntity>> GetAll() => throw new NotSupportedException();

		public Task<IReadOnlyList<VariableEntity>> GetByScope(VariableScope scope, string? scopeRefId)
			=> throw new NotSupportedException();

		public Task<VariableEntity?> GetById(Guid id) => throw new NotSupportedException();

		public Task<VariableEntity?> Resolve(string name, VariableScope contextScope, string? contextScopeRefId)
			=> throw new NotSupportedException();

		public Task<Result<VariableEntity, VariableError>> CreateUserVariable(string name,
			VariableScope scope,
			string? scopeRefId,
			DomainVariableType type,
			object? initialValue,
			int? decimalPlaces) => throw new NotSupportedException();

		public Task<Result<VariableEntity, VariableError>> SetValue(Guid id,
			object? value,
			CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<Result<VariableEntity, VariableError>> UpdateUserVariable(Guid id,
			string? name,
			int? decimalPlaces) => throw new NotSupportedException();

		public Task<Result<VariableError>> DeleteUserVariable(Guid id) => throw new NotSupportedException();

		public Task<Result<VariableEntity, VariableError>> CreateIntegrationVariable(string integrationId,
			string name,
			VariableScope scope,
			string? scopeRefId,
			DomainVariableType type,
			object? initialValue,
			int? decimalPlaces,
			string? definitionId = null,
			VariableDeclaration? declaration = null,
			VariableUpdateMode updateMode = VariableUpdateMode.Polled) => throw new NotSupportedException();

		public Task<Result<VariableEntity, VariableError>> MaterializeCatalogVariable(string integrationId,
			string resourceId,
			string name,
			VariableType type,
			int? decimalPlaces,
			VariableDeclaration? declaration = null) =>
			throw new NotSupportedException();

		public Task<VariableEntity?> GetByDefinition(QualifiedId definitionId) =>
			Task.FromResult<VariableEntity?>(null);

		public Task<Result<VariableEntity, VariableError>> ReportIntegrationVariableValue(string integrationId,
			Guid id,
			object? value,
			VariableBounds? bounds = null) => throw new NotSupportedException();

		public Task<Result<VariableError>> SetIntegrationVariableAvailability(string integrationId,
			Guid id,
			bool available) => throw new NotSupportedException();

		public Task<Result<VariableError>> DeleteIntegrationVariable(string integrationId, Guid id)
			=> throw new NotSupportedException();

		public Task<IReadOnlyList<VariableEntity>> GetByOwnerIntegration(string integrationId)
			=> throw new NotSupportedException();

		public Task DeleteByScopeInstance(VariableScope scope, string scopeRefId)
			=> throw new NotSupportedException();

		public Task UpsertWidgetVariable(VariableScope scope,
			string scopeRefId,
			string name,
			DomainVariableType type,
			object? value) => throw new NotSupportedException();

		public Task RemoveWidgetVariable(VariableScope scope, string scopeRefId, string name)
			=> throw new NotSupportedException();
	}
}
