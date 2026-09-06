using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Events;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Infrastructure.Backups;

public sealed class BackupProgressReporter : IBackupProgressReporter
{
	private static readonly TimeSpan _minimumInterval = TimeSpan.FromMilliseconds(250);

	private readonly IServiceScopeFactory _scopeFactory;
	private readonly TimeProvider _time;
	private readonly Lock _sync = new();

	private BackupOperationStatus _current;
	private DateTimeOffset _lastPublished = DateTimeOffset.MinValue;

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
		bool publish;

		lock (_sync)
		{
			var stageChanged = _current.Stage != status.Stage || _current.OperationId != status.OperationId;
			var now = _time.GetUtcNow();
			publish = stageChanged || now - _lastPublished >= _minimumInterval;
			_current = status;

			if (publish)
			{
				_lastPublished = now;
			}
		}

		if (!publish)
		{
			return;
		}

		// Fire and forget on purpose: a backup must not stall or fail because a UI client is slow.
		_ = Publish(status);
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
}
