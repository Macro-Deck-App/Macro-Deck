using System.Collections.Concurrent;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace MacroDeckHost.Application.Timers;

public sealed class TimerWidgetVariableWriter
{
	public const string CountdownRemainingSeconds = "countdown_remaining_seconds";

	public const string CountdownRunning = "countdown_running";

	public const string CountdownFinished = "countdown_finished";

	public const string StopwatchElapsedSeconds = "stopwatch_elapsed_seconds";

	public const string StopwatchRunning = "stopwatch_running";

	private readonly TimerWidgetStore _store;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILogger _logger;
	private readonly ConcurrentDictionary<Guid, WriteGate> _gates = new();

	public TimerWidgetVariableWriter(TimerWidgetStore store, IServiceScopeFactory scopeFactory, ILogger logger)
	{
		_store = store;
		_scopeFactory = scopeFactory;
		_logger = logger.ForContext<TimerWidgetVariableWriter>();
	}

	public static IReadOnlyList<string> NamesFor(TimerWidgetKind kind)
		=> kind == TimerWidgetKind.Countdown
			? [CountdownRemainingSeconds, CountdownRunning, CountdownFinished]
			: [StopwatchElapsedSeconds, StopwatchRunning];

	public async Task WriteAsync(TimerWidgetSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot);

		var gate = _gates.GetOrAdd(snapshot.WidgetId, _ => new WriteGate());
		await gate.Semaphore.WaitAsync().ConfigureAwait(false);

		try
		{
			// A write queued before the widget was deleted must not bring its variables back.
			if (snapshot.Version <= gate.LastVersion || !_store.Contains(snapshot.WidgetId))
			{
				return;
			}

			await using var scope = _scopeFactory.CreateAsyncScope();
			var variables = scope.ServiceProvider.GetRequiredService<IVariableService>();
			var scopeRefId = snapshot.WidgetId.ToString();
			var now = _store.Now;

			if (snapshot.Kind == TimerWidgetKind.Countdown)
			{
				await variables.UpsertWidgetVariable(VariableScope.Widget,
						scopeRefId,
						CountdownRemainingSeconds,
						VariableType.Numeric,
						(decimal)snapshot.RemainingSecondsAt(now))
					.ConfigureAwait(false);
				await variables.UpsertWidgetVariable(VariableScope.Widget,
						scopeRefId,
						CountdownRunning,
						VariableType.Boolean,
						snapshot.IsRunning)
					.ConfigureAwait(false);
				await variables.UpsertWidgetVariable(VariableScope.Widget,
						scopeRefId,
						CountdownFinished,
						VariableType.Boolean,
						snapshot.IsFinished)
					.ConfigureAwait(false);
			}
			else
			{
				await variables.UpsertWidgetVariable(VariableScope.Widget,
						scopeRefId,
						StopwatchElapsedSeconds,
						VariableType.Numeric,
						(decimal)snapshot.ElapsedSecondsAt(now))
					.ConfigureAwait(false);
				await variables.UpsertWidgetVariable(VariableScope.Widget,
						scopeRefId,
						StopwatchRunning,
						VariableType.Boolean,
						snapshot.IsRunning)
					.ConfigureAwait(false);
			}

			gate.LastVersion = snapshot.Version;

			// The widget can be deleted while the upserts above were running.
			if (!_store.Contains(snapshot.WidgetId))
			{
				await RemoveNamesAsync(variables, snapshot.WidgetId, snapshot.Kind).ConfigureAwait(false);
			}
		}
#pragma warning disable CA1031 // A failed variable write must not stop the timer or the flows it raises.
		catch (Exception exception)
#pragma warning restore CA1031
		{
			_logger.Warning(exception, "Failed to write the timer variables of widget {WidgetId}", snapshot.WidgetId);
		}
		finally
		{
			gate.Semaphore.Release();
		}

		if (!_store.Contains(snapshot.WidgetId))
		{
			_gates.TryRemove(new KeyValuePair<Guid, WriteGate>(snapshot.WidgetId, gate));
		}
	}

	public async Task RemoveAsync(Guid widgetId, TimerWidgetKind kind)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();

		await RemoveNamesAsync(scope.ServiceProvider.GetRequiredService<IVariableService>(), widgetId, kind)
			.ConfigureAwait(false);
	}

	public void Forget(Guid widgetId) => _gates.TryRemove(widgetId, out _);

	private static async Task RemoveNamesAsync(IVariableService variables, Guid widgetId, TimerWidgetKind kind)
	{
		foreach (var name in NamesFor(kind))
		{
			await variables.RemoveWidgetVariable(VariableScope.Widget, widgetId.ToString(), name).ConfigureAwait(false);
		}
	}

	private sealed class WriteGate
	{
		public SemaphoreSlim Semaphore { get; } = new(1, 1);

		public long LastVersion { get; set; }
	}
}
