using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Integrations;
using MacroDeckHost.Integrations;
using MacroDeck.Sdk;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public class IntegrationStartupBackgroundService : HostReadyBackgroundService
{
	private readonly IIntegrationRegistry _registry;
	private readonly IServiceScopeFactory _serviceScopeFactory;
	private readonly IUserNotificationStore _userNotificationStore;
	private readonly IntegrationInitializer _initializer;
	private readonly IMediator _mediator;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;

	public IntegrationStartupBackgroundService(
		IHostApplicationLifetime lifetime,
		IIntegrationRegistry registry,
		IServiceScopeFactory serviceScopeFactory,
		IUserNotificationStore userNotificationStore,
		IntegrationInitializer initializer,
		IMediator mediator,
		TimeProvider timeProvider,
		ILogger logger)
		: base(lifetime)
	{
		_registry = registry;
		_serviceScopeFactory = serviceScopeFactory;
		_userNotificationStore = userNotificationStore;
		_initializer = initializer;
		_mediator = mediator;
		_timeProvider = timeProvider;
		_logger = logger;
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		_logger.Information("Starting integration discovery and initialization");

		var integrations = IntegrationDiscovery.DiscoverIntegrations(_logger).AsEnumerable();

		if (!DeveloperIntegrationsEnabled())
		{
			integrations = integrations.Where(i => i is not ISampleIntegration);
		}

		var pending = await RegisterAllAsync(integrations);
		await InitializeAllAsync(pending, stoppingToken);

		_logger.Information("All integrations initialized");
	}

	public override async Task StopAsync(CancellationToken cancellationToken)
	{
		try
		{
			await base.StopAsync(cancellationToken);
		}
		finally
		{
			// A cancelled generic-host stop must not silently skip the only async durability boundary.
			// ShutdownAsync has no cancellation token by contract, so the host's outer stop timeout still
			// decides how long the process is allowed to remain alive.
			await ShutdownIntegrationsAsync(_registry, _logger, _initializer.AttemptedIds, _timeProvider);
		}
	}

	internal static async Task ShutdownIntegrationsAsync(
		IIntegrationRegistry registry,
		ILogger logger,
		IReadOnlySet<string> attemptedIds,
		TimeProvider timeProvider)
	{
		var toShutDown = registry.Integrations.Where(integration =>
			registry.IsEnabled(integration.Id) &&
			(integration.IsInitialized || attemptedIds.Contains(integration.Id)));

		await Task.WhenAll(toShutDown.Select(integration => ShutdownOneAsync(integration, logger, timeProvider)));
	}

	private static async Task ShutdownOneAsync(IIntegration integration, ILogger logger, TimeProvider timeProvider)
	{
		try
		{
			// An unbounded ShutdownAsync on a hung integration would turn a quit into minutes once summed
			// sequentially across ~20 integrations - hence the timeout (shared with the other shutdown call
			// sites via IntegrationShutdownRunner) and the parallel fan-out below. ShutdownAsync is also the
			// only async durability boundary an integration gets, so abandoning it here on timeout may drop
			// a pending write; that is a deliberate trade against hanging the whole quit forever.
			await IntegrationShutdownRunner.RunAsync(integration, timeProvider, CancellationToken.None);
			logger.Information("Integration '{IntegrationId}' shut down cleanly", integration.Id);
		}
		catch (TimeoutException)
		{
			logger.Error("Integration '{IntegrationId}' did not shut down within {Timeout} and was abandoned",
				integration.Id,
				IntegrationShutdownRunner.Timeout);
		}
		catch (Exception ex)
		{
			// One provider must not keep the remaining providers from flushing their own durable state.
			logger.Error(ex, "Failed to shut down integration '{IntegrationId}'", integration.Id);
		}
	}

	internal readonly record struct PendingIntegration(IIntegration Integration, string Name);

	internal async Task<IReadOnlyList<PendingIntegration>> RegisterAllAsync(IEnumerable<IIntegration> integrations)
	{
		var pending = new List<PendingIntegration>();

		foreach (var integration in integrations)
		{
			// A notification is one finished sentence for the user, so the integration's name is resolved
			// once here rather than carried as a reference into text the host composes.
			var integrationName = await ActiveLocalization.Resolve(_serviceScopeFactory, integration.Name);

			var registration = await _registry.RegisterAsync(integration);
			if (!registration.Registered)
			{
				RaiseRegistrationRejected(integration, integrationName, registration);
				continue;
			}

			if (!_registry.IsEnabled(integration.Id))
			{
				_logger.Information("Integration '{IntegrationId}' is disabled; skipping initialization",
					integration.Id);
				continue;
			}

			pending.Add(new PendingIntegration(integration, integrationName));
		}

		return pending;
	}

	internal Task InitializeAllAsync(IReadOnlyList<PendingIntegration> pending, CancellationToken stoppingToken)
		=> Task.WhenAll(pending.Select(p => InitializeOneAsync(p, stoppingToken)));

	private async Task InitializeOneAsync(PendingIntegration pending, CancellationToken stoppingToken)
	{
		var outcome = await _initializer.InitializeAsync(pending.Integration, pending.Name, stoppingToken);

		if (outcome == IntegrationInitializationOutcome.Initialized)
		{
			// Announce the instances this integration now provides so live broadcasters (e.g. the weather
			// station list) push them immediately instead of at their next polling interval. Kestrel accepts
			// client connections before startup finishes, so without this a freshly (re)started host shows an
			// empty widget until the broadcaster's next tick - the cold-start half of issue #94.
			await _mediator.Publish(new IntegrationStateChangedNotification(pending.Integration.Id), stoppingToken);
		}
	}

	private void RaiseRegistrationRejected(
		IIntegration integration,
		string integrationName,
		IntegrationRegistrationResult registration)
	{
		_logger.Error("Integration '{IntegrationId}' was not registered: {Reason}",
			integration.Id,
			registration.Failure == IntegrationRegistrationFailure.DuplicateIntegrationId
				? $"Another integration is already registered as '{integration.Id}'."
				: registration.Describe());

		IntegrationRegistrationRejectionNotifier.Raise(_userNotificationStore,
			integration.Id,
			integrationName,
			registration);
	}

	private static bool DeveloperIntegrationsEnabled()
		=> string.Equals(Environment.GetEnvironmentVariable("ENABLE_EXAMPLE_INTEGRATION"),
			"true",
			StringComparison.OrdinalIgnoreCase);
}
