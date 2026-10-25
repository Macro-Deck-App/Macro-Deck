using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Domain.Enums;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Infrastructure.Backups;

public sealed class BackupProgressReporter : IBackupProgressReporter
{
	private static readonly TimeSpan _minimumInterval = TimeSpan.FromMilliseconds(250);
	private static readonly TimeSpan _rateWindow = TimeSpan.FromSeconds(5);

	private readonly IServiceScopeFactory _scopeFactory;
	private readonly TimeProvider _time;
	private readonly Lock _sync = new();

	private BackupOperationStatus _current;
	private DateTimeOffset _lastPublished = DateTimeOffset.MinValue;
	private RateSample? _rateSample;
	private int? _eta;
	private Task _publishTail = Task.CompletedTask;

	public BackupProgressReporter(IServiceScopeFactory scopeFactory, TimeProvider time)
	{
		_scopeFactory = scopeFactory;
		_time = time;
		_current = BackupOperationStatus.Idle(time.GetUtcNow());
	}

	public BackupOperationStatus Current
	{
		get
		{
			lock (_sync)
			{
				return _current;
			}
		}
	}

	public void Report(BackupOperationStatus status)
	{
		lock (_sync)
		{
			var stageChanged = _current.Stage != status.Stage || _current.OperationId != status.OperationId;
			var now = _time.GetUtcNow();
			status = status with { EtaSeconds = EstimateEta(status, stageChanged, now) };
			_current = status;

			if (!stageChanged && now - _lastPublished < _minimumInterval)
			{
				return;
			}

			_lastPublished = now;

			// Fire and forget on purpose: a backup must not stall or fail because a UI client is slow. The
			// chain keeps events in report order, so a late sample never lands after its terminal stage.
			_publishTail = _publishTail.ContinueWith(_ => Publish(status), TaskScheduler.Default).Unwrap();
		}
	}

	private static bool IsTerminal(BackupOperationStage stage)
	{
		return stage is BackupOperationStage.Completed or BackupOperationStage.Failed;
	}

	private int? EstimateEta(BackupOperationStatus status, bool stageChanged, DateTimeOffset now)
	{
		if (IsTerminal(status.Stage) || status.ProgressFraction is not { } fraction)
		{
			_rateSample = null;
			_eta = null;
			return null;
		}

		if (stageChanged || _rateSample is null)
		{
			_rateSample = new RateSample(now, fraction);
			_eta = null;
			return null;
		}

		var sample = _rateSample.Value;
		var elapsed = now - sample.At;
		if (elapsed >= _rateWindow && fraction > sample.Fraction)
		{
			var perSecond = (fraction - sample.Fraction) / elapsed.TotalSeconds;
			_eta = (int)Math.Ceiling(Math.Max(0, 1 - fraction) / perSecond);
			_rateSample = new RateSample(now, fraction);
		}

		return _eta;
	}

	private async Task Publish(BackupOperationStatus status)
	{
		try
		{
			await using var scope = _scopeFactory.CreateAsyncScope();
			var mediator = scope.ServiceProvider.GetRequiredService<IPublisher>();
			await mediator.Publish(new BackupOperationProgressNotification(status));
		}
		catch (Exception e) when (e is not OperationCanceledException)
		{
		}
	}

	private readonly record struct RateSample(DateTimeOffset At, double Fraction);
}
