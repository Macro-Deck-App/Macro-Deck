using System.Collections.Concurrent;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Logging;
using MacroDeckHost.Application.Plugins.Runtime;
using Serilog;

namespace MacroDeckHost.Application.Plugins.Logging;

public enum PluginLogIngestOutcome
{
	Ingested,

	BatchTooLarge
}

public sealed record PluginLogIngestResult
{
	public required PluginLogIngestOutcome Outcome { get; init; }

	public int IngestedCount { get; init; }

	public int RateLimitedCount { get; init; }

	public bool RateLimitTransitionedToLimited { get; init; }

	public static PluginLogIngestResult TooLarge { get; } = new() { Outcome = PluginLogIngestOutcome.BatchTooLarge };
}

public interface IPluginLogIngestor
{
	PluginLogIngestResult Ingest(string pluginId, string sessionId, IReadOnlyList<LogEventDto> events);

	void Evict(string pluginId);
}

public sealed class PluginLogIngestor : IPluginLogIngestor
{
	private readonly IPluginLogRateLimiter _rateLimiter;
	private readonly IPluginSessionRegistry _sessionRegistry;
	private readonly IPluginSupervisor _pluginSupervisor;
	private readonly TimeProvider _timeProvider;
	private readonly Func<ILogger> _loggerAccessor;
	private readonly ConcurrentDictionary<string, bool> _rateLimitedState = new(StringComparer.Ordinal);

	public PluginLogIngestor(
		IPluginLogRateLimiter rateLimiter,
		IPluginSessionRegistry sessionRegistry,
		IPluginSupervisor pluginSupervisor,
		TimeProvider timeProvider)
		: this(rateLimiter, sessionRegistry, pluginSupervisor, timeProvider, () => Log.Logger)
	{
	}

	internal PluginLogIngestor(
		IPluginLogRateLimiter rateLimiter,
		IPluginSessionRegistry sessionRegistry,
		IPluginSupervisor pluginSupervisor,
		TimeProvider timeProvider,
		Func<ILogger> loggerAccessor)
	{
		_rateLimiter = rateLimiter;
		_sessionRegistry = sessionRegistry;
		_pluginSupervisor = pluginSupervisor;
		_timeProvider = timeProvider;
		_loggerAccessor = loggerAccessor;
	}

	public PluginLogIngestResult Ingest(string pluginId, string sessionId, IReadOnlyList<LogEventDto> events)
	{
		ArgumentNullException.ThrowIfNull(pluginId);
		ArgumentNullException.ThrowIfNull(sessionId);
		ArgumentNullException.ThrowIfNull(events);

		if (events.Count > ProtocolLimits.MaxLogEventsPerBatch)
		{
			return PluginLogIngestResult.TooLarge;
		}

		var declaredVersion = _sessionRegistry.Snapshot()
			.FirstOrDefault(session => string.Equals(session.SessionId, sessionId, StringComparison.Ordinal))
			?.DeclaredVersion;
		var processId = _pluginSupervisor.Snapshot()
			.FirstOrDefault(snapshot => string.Equals(snapshot.PluginId, pluginId, StringComparison.Ordinal))
			?.ProcessId;

		var logger = _loggerAccessor();
		var ingested = 0;
		var rateLimited = 0;

		foreach (var dto in events)
		{
			if (!_rateLimiter.TryAcquire(pluginId))
			{
				rateLimited++;
				continue;
			}

			var level = PluginLogEventFactory.MapLevel(dto.Level);
			if (!logger.IsEnabled(level))
			{
				continue;
			}

			var logEvent
				= PluginLogEventFactory.Create(pluginId, sessionId, declaredVersion, processId, dto, _timeProvider);
			logger.Write(logEvent);
			ingested++;
		}

		var isLimitedNow = rateLimited > 0;

		var transitioned = false;
		_rateLimitedState.AddOrUpdate(pluginId,
			addValueFactory: _ =>
			{
				transitioned = isLimitedNow;
				return isLimitedNow;
			},
			updateValueFactory: (_, wasLimited) =>
			{
				transitioned = isLimitedNow && !wasLimited;
				return isLimitedNow;
			});

		return new PluginLogIngestResult
		{
			Outcome = PluginLogIngestOutcome.Ingested,
			IngestedCount = ingested,
			RateLimitedCount = rateLimited,
			RateLimitTransitionedToLimited = transitioned
		};
	}

	public void Evict(string pluginId) => _rateLimitedState.TryRemove(pluginId, out _);
}
