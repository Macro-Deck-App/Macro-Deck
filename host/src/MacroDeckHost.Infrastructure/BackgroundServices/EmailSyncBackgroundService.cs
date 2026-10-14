using MacroDeckHost.Application.Email;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Services;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class EmailSyncBackgroundService : HostReadyBackgroundService
{
	public static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

	public static readonly TimeSpan CoalesceWindow = TimeSpan.FromMilliseconds(500);

	private static readonly TimeSpan _retryAfterFailure = TimeSpan.FromMinutes(1);

	private readonly IEmailInboxCache _cache;
	private readonly IEmailInboxRegistry _registry;
	private readonly IIntegrationRegistry _integrations;
	private readonly IEmailSyncSignal _signal;
	private readonly StartupReadiness _readiness;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;

	public EmailSyncBackgroundService(
		IHostApplicationLifetime lifetime,
		IEmailInboxCache cache,
		IEmailInboxRegistry registry,
		IIntegrationRegistry integrations,
		IEmailSyncSignal signal,
		StartupReadiness readiness,
		TimeProvider time,
		ILogger logger)
		: base(lifetime)
	{
		_cache = cache;
		_registry = registry;
		_integrations = integrations;
		_signal = signal;
		_readiness = readiness;
		_time = time;
		_logger = logger.ForContext<EmailSyncBackgroundService>();
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		_integrations.AvailabilityChanged += OnAvailabilityChanged;
		try
		{
			await _readiness.WhenReady.WaitAsync(stoppingToken);

			// A provider registered before this service started raised its changes into nothing, so the
			// first pass subscribes and reads everything.
			_registry.ReconcileSubscriptions();
			var request = EmailSyncRequest.Everything;
			var nextPoll = _time.GetUtcNow() + PollInterval;

			while (!stoppingToken.IsCancellationRequested)
			{
				try
				{
					await _cache.SyncAsync(request, stoppingToken);
				}
				catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
				{
					_logger.Error(exception, "Email sync failed; retrying in {Delay}", _retryAfterFailure);
					nextPoll = _time.GetUtcNow() + _retryAfterFailure;
				}

				var now = _time.GetUtcNow();
				var wakeAt = _cache.NextRetryAt is { } retryAt && retryAt < nextPoll ? retryAt : nextPoll;
				var wait = wakeAt > now ? wakeAt - now : TimeSpan.Zero;

				if (await _signal.WaitAsync(wait, _time, stoppingToken))
				{
					await Task.Delay(CoalesceWindow, _time, stoppingToken);
				}

				request = _signal.TakePending();
				if (_time.GetUtcNow() >= nextPoll)
				{
					request = EmailSyncRequest.Everything;
					nextPoll = _time.GetUtcNow() + PollInterval;
				}
			}
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
		}
		finally
		{
			_integrations.AvailabilityChanged -= OnAvailabilityChanged;
		}
	}

	private void OnAvailabilityChanged(object? sender, IntegrationAvailabilityChangedEventArgs e)
	{
		_registry.ReconcileSubscriptions();

		if (_registry.IsEmailIntegration(e.IntegrationId)
			|| _cache.Snapshot.Inboxes.Any(state => state.Inbox.IntegrationId == e.IntegrationId))
		{
			_signal.Request(e.IntegrationId, null);
		}
	}
}
