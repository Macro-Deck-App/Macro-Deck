using System.Collections.Concurrent;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Capabilities.Ui;
using MacroDeck.Sdk.Notifications;

namespace MacroDeck.Plugin.Hosting.Capabilities.Ui;

internal sealed class NotificationResultStore
{
	private const int MaxUnclaimed = 256;

	private readonly ConcurrentDictionary<Guid, TaskCompletionSource<NotificationResult>> _pending = new();

	public Task<NotificationResult> Register(Guid notificationId)
	{
		var completion = _pending.GetOrAdd(notificationId, static _ => NewCompletion());

		// A result can beat the registration, because the host answers the open call and may close the
		// notification right away. An already completed entry is claimed here instead of leaking.
		if (completion.Task.IsCompleted)
		{
			_pending.TryRemove(notificationId, out _);
		}
		else
		{
			_ = completion.Task.ContinueWith(_ => _pending.TryRemove(notificationId, out var removed),
				CancellationToken.None,
				TaskContinuationOptions.ExecuteSynchronously,
				TaskScheduler.Default);
		}

		return completion.Task;
	}

	public void Complete(UiNotificationResultArguments result)
	{
		ArgumentNullException.ThrowIfNull(result);

		_pending.GetOrAdd(result.NotificationId, static _ => NewCompletion()).TrySetResult(Map(result));

		if (_pending.Count > MaxUnclaimed)
		{
			foreach (var (id, completion) in _pending)
			{
				if (completion.Task.IsCompleted)
				{
					_pending.TryRemove(id, out _);
				}
			}
		}
	}

	public void CancelAll(NotificationCloseReason reason)
	{
		foreach (var completion in _pending.Values)
		{
			completion.TrySetResult(new NotificationResult { CloseReason = reason });
		}
	}

	private static NotificationResult Map(UiNotificationResultArguments result)
	{
		return new NotificationResult
		{
			CloseReason = ParseReason(result.CloseReason),
			ActionId = result.ActionId,
			ClientId = result.ClientId,
			Values = result.Values ?? new Dictionary<string, System.Text.Json.JsonElement>()
		};
	}

	private static NotificationCloseReason ParseReason(string? reason)
	{
		return reason switch
		{
			NotificationsWire.CloseReason.Action => NotificationCloseReason.Action,
			NotificationsWire.CloseReason.Timeout => NotificationCloseReason.Timeout,
			NotificationsWire.CloseReason.Programmatic => NotificationCloseReason.Programmatic,
			NotificationsWire.CloseReason.OwnerGone => NotificationCloseReason.OwnerGone,
			NotificationsWire.CloseReason.HostUnavailable => NotificationCloseReason.HostUnavailable,
			_ => NotificationCloseReason.Dismissed
		};
	}

	private static TaskCompletionSource<NotificationResult> NewCompletion()
	{
		return new TaskCompletionSource<NotificationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
	}
}
