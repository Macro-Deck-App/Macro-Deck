using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Logging;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class LogTailBackgroundService : BackgroundService
{
	public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

	private const int MaxBatchSize = 200;

	private readonly ILogFileReader _reader;
	private readonly LogStreamSubscriptionTracker _subscriptions;
	private readonly IUiTransport _transport;
	private readonly ILogLevelState _logLevelState;
	private readonly TimeSpan _interval;

	public LogTailBackgroundService(
		ILogFileReader reader,
		LogStreamSubscriptionTracker subscriptions,
		IUiTransport transport,
		ILogLevelState logLevelState)
		: this(reader, subscriptions, transport, logLevelState, PollInterval)
	{
	}

	internal LogTailBackgroundService(
		ILogFileReader reader,
		LogStreamSubscriptionTracker subscriptions,
		IUiTransport transport,
		ILogLevelState logLevelState,
		TimeSpan interval)
	{
		_reader = reader;
		_subscriptions = subscriptions;
		_transport = transport;
		_logLevelState = logLevelState;
		_interval = interval;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(_interval);

		while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
		{
			await Tick(stoppingToken).ConfigureAwait(false);
		}
	}

	internal async Task Tick(CancellationToken cancellationToken)
	{
		if (_subscriptions.MinimumPosition() is not { } from)
		{
			return;
		}

		var batch = _reader.ReadAfter(from);
		var minimum = _logLevelState.Minimum;


		foreach (var (connectionId, subscription) in _subscriptions.Snapshot())
		{
			var (entries, sources, next) = Select(batch, subscription, minimum);
			if (entries.Count > 0 || sources.Count > 0)
			{
				await _transport.SendToGroup(LogStreamGroups.For(connectionId),
						new LogEntriesAppendedNotification { Entries = entries, Sources = sources },
						cancellationToken)
					.ConfigureAwait(false);
			}

			_subscriptions.Advance(connectionId, subscription.Position, next);
		}
	}

	private static (List<LogEntry> Entries, List<LogSourceSummary> Sources, LogCursor Next) Select(
		LogTailBatch batch,
		LogSubscription subscription,
		LogEntryLevel bootstrapperMinimum)
	{
		var selected = new List<LogEntry>();
		var keys = new HashSet<(LogEntrySource Source, string? SourceId)>();
		var capped = false;

		var resume = new Dictionary<LogFileKind, LogReadPosition>();

		foreach (var entry in batch.Entries)
		{
			if (!LogEntryId.TryParse(entry.Id, out var stream, out var position))
			{
				continue;
			}

			var seen = subscription.Position.For(stream);
			if (seen.HasFile && position.CompareTo(seen) < 0)
			{
				continue;
			}

			if (capped || selected.Count >= MaxBatchSize)
			{
				capped = true;
				resume.TryAdd(stream, position);

				continue;
			}

			if (entry.Source != LogEntrySource.Bootstrapper || entry.Level >= bootstrapperMinimum)
			{
				keys.Add((entry.Source, entry.Source == LogEntrySource.Integration ? entry.SourceId : null));
			}

			if (subscription.Query.Matches(entry, bootstrapperMinimum))
			{
				selected.Add(entry);
			}
		}

		var sources = keys
			.Select(key => new LogSourceSummary { Source = key.Source, SourceId = key.SourceId })
			.ToList();

		if (!capped)
		{
			return (selected, sources, batch.Position);
		}

		var next = batch.Position;
		foreach (var stream in new[] { LogFileKind.Host, LogFileKind.Bootstrapper })
		{
			if (resume.TryGetValue(stream, out var position))
			{
				next = next.With(stream, position);
			}
		}

		return (selected, sources, next);
	}
}
