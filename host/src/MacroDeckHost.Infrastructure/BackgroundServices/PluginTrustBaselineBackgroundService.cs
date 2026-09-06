using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Application.Plugins.Trust;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

/// <summary>Establishes the one-time trust baseline (issue #610, decision 5): every plugin already
/// installed the first time this runs is grandfathered with an <c>Unsigned</c> trust record, so it keeps
/// running under enforcement rather than being treated as anomalous. If a plugin is launched before this
/// runs, <see cref="PluginTrustGate" />'s own "missing record, before baseline" case backfills it the same
/// way - the two are redundant with each other by design, not racing for correctness.</summary>
public sealed class PluginTrustBaselineBackgroundService : HostReadyBackgroundService
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;

	public PluginTrustBaselineBackgroundService(IHostApplicationLifetime lifetime,
		IServiceScopeFactory scopeFactory,
		TimeProvider timeProvider,
		ILogger logger)
		: base(lifetime)
	{
		_scopeFactory = scopeFactory;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<PluginTrustBaselineBackgroundService>();
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		try
		{
			await EstablishBaseline(stoppingToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// Not rethrown: the default BackgroundServiceExceptionBehavior would stop the host over a
			// grandfathering step that is both idempotent and retried on the next launch - see the class
			// summary. A DB error here must never take Macro Deck down.
			_logger.Error(ex, "Failed to establish the plugin trust baseline; it will be retried next launch.");
		}
	}

	private async Task EstablishBaseline(CancellationToken stoppingToken)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var baseline = scope.ServiceProvider.GetRequiredService<IPluginTrustBaseline>();

		if (await baseline.Exists(stoppingToken))
		{
			return;
		}

		var catalog = scope.ServiceProvider.GetRequiredService<IPluginInstallationCatalog>();
		var trustRecords = scope.ServiceProvider.GetRequiredService<IPluginTrustRecordRepository>();
		var now = _timeProvider.GetUtcNow().UtcDateTime;
		var grandfathered = 0;

		foreach (var plugin in catalog.Discover())
		{
			if (plugin.ActiveVersion is not { } active)
			{
				continue;
			}

			if (await trustRecords.GetVersion(plugin.PluginId, active.Version) is not null)
			{
				continue;
			}

			await trustRecords.Upsert(plugin.PluginId,
				active.Version,
				PluginTrustRecordVerdicts.Unsigned,
				certificateId: null,
				now);
			grandfathered++;
		}

		await baseline.Establish(stoppingToken);
		_logger.Information("Established the plugin trust baseline, grandfathering {Count} plugin(s).",
			grandfathered);
	}
}
