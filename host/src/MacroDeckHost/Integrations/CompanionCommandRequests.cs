using System.Collections.Concurrent;
using MacroDeckHost.Integrations.Companion;

namespace MacroDeckHost.Integrations;

public enum CompanionRequestKind
{
	Screenshot,
	Command
}

public sealed class CompanionCommandRequests
{
	public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);
	public static readonly TimeSpan ClaimedCommandDeadline = TimeSpan.FromSeconds(10);
	public static readonly TimeSpan ClaimedScreenshotDeadline = TimeSpan.FromSeconds(60);

	private readonly ConcurrentDictionary<(Guid DeviceId, string RequestId), PendingRequest> _pending = new();

	private readonly TimeProvider _timeProvider;

	public CompanionCommandRequests(TimeProvider timeProvider)
	{
		_timeProvider = timeProvider;
	}

	[Flags]
	private enum State
	{
		Pending = 1,
		Claimed = 2,
		Finished = 4,
		Open = Pending | Claimed
	}

	public async Task<CompanionCommandResult> RequestAsync(Guid deviceId,
		CompanionRequestKind kind,
		bool requiresClaim,
		Func<string, Task<bool>> send,
		Action<string> abandoned,
		CancellationToken cancellationToken)
	{
		var key = (deviceId, Guid.NewGuid().ToString("N"));
		var pending = new PendingRequest(kind, requiresClaim, abandoned);
		_pending[key] = pending;
		try
		{
			if (!await SendAsync(send, key, pending))
			{
				return CompanionCommandResult.Failed(CompanionCommandFailure.NotConnected);
			}

			pending.Deadline = _timeProvider.CreateTimer(_ => GiveUp(key,
					pending,
					CompanionCommandResult.Failed(CompanionCommandFailure.TimedOut)),
				null,
				Deadline,
				Timeout.InfiniteTimeSpan);
			return await pending.Completion.Task.WaitAsync(cancellationToken);
		}
		catch (OperationCanceledException) when (!GiveUp(key, pending, null))
		{
			return await pending.Completion.Task;
		}
		finally
		{
			TryFinish(key, pending, State.Pending, null);
			pending.Deadline?.Dispose();
			pending.ClaimedDeadline?.Dispose();
		}
	}

	private async Task<bool> SendAsync(Func<string, Task<bool>> send,
		(Guid, string RequestId) key,
		PendingRequest pending)
	{
		try
		{
			return await send(key.RequestId);
		}
		catch (Exception ex) when (ex is not OperationCanceledException &&
			!TryFinish(key, pending, State.Pending, null))
		{
			return true;
		}
	}

	public bool IsAwaitingAnswer(Guid deviceId, string requestId, CompanionRequestKind kind)
		=> _pending.TryGetValue((deviceId, requestId), out var pending) &&
			pending.Kind == kind &&
			(pending.State & pending.AnswerableFrom) != 0;

	public bool TryClaim(Guid deviceId, string requestId, CompanionRequestKind kind)
	{
		var key = (deviceId, requestId);
		if (!_pending.TryGetValue(key, out var pending) || pending.Kind != kind || !pending.RequiresClaim)
		{
			return false;
		}

		lock (pending)
		{
			if (pending.State != State.Pending)
			{
				return false;
			}

			pending.State = State.Claimed;
			pending.ClaimedDeadline = _timeProvider.CreateTimer(
				_ => TryFinish(key, pending, State.Claimed, pending.AfterClaim(CompanionCommandFailure.TimedOut)),
				null,
				kind == CompanionRequestKind.Command ? ClaimedCommandDeadline : ClaimedScreenshotDeadline,
				Timeout.InfiniteTimeSpan);
		}

		return true;
	}

	public bool TryComplete(Guid deviceId,
		string requestId,
		CompanionCommandResult result,
		CompanionRequestKind kind,
		bool afterRunning)
		=> _pending.TryGetValue((deviceId, requestId), out var pending) &&
			pending.Kind == kind &&
			TryFinish((deviceId, requestId), pending, afterRunning ? pending.AnswerableFrom : State.Open, result);

	public void FailDevice(Guid deviceId, CompanionCommandFailure failure)
	{
		foreach (var (key, pending) in _pending.Where(entry => entry.Key.DeviceId == deviceId))
		{
			if (!TryFinish(key, pending, State.Pending, CompanionCommandResult.Failed(failure)))
			{
				TryFinish(key, pending, State.Claimed, pending.AfterClaim(failure));
			}
		}
	}

	private bool GiveUp((Guid, string RequestId) key, PendingRequest pending, CompanionCommandResult? result)
	{
		if (!TryFinish(key, pending, State.Pending, result))
		{
			return false;
		}

		pending.Abandoned(key.RequestId);
		return true;
	}

	private bool TryFinish((Guid, string) key, PendingRequest pending, State from, CompanionCommandResult? result)
	{
		lock (pending)
		{
			if ((pending.State & from) == 0)
			{
				return false;
			}

			pending.State = State.Finished;
		}

		_pending.TryRemove(KeyValuePair.Create(key, pending));
		if (result is null)
		{
			pending.Completion.TrySetCanceled();
		}
		else
		{
			pending.Completion.TrySetResult(result);
		}

		return true;
	}

	private sealed class PendingRequest
	{
		public PendingRequest(CompanionRequestKind kind, bool requiresClaim, Action<string> abandoned)
		{
			Kind = kind;
			RequiresClaim = requiresClaim;
			Abandoned = abandoned;
		}

		public CompanionRequestKind Kind { get; }

		public bool RequiresClaim { get; }

		public Action<string> Abandoned { get; }

		public State AnswerableFrom => RequiresClaim ? State.Claimed : State.Open;

		public TaskCompletionSource<CompanionCommandResult> Completion { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public State State { get; set; } = State.Pending;

		public ITimer? Deadline { get; set; }

		public ITimer? ClaimedDeadline { get; set; }

		public CompanionCommandResult AfterClaim(CompanionCommandFailure failure)
			=> Kind == CompanionRequestKind.Command
				? CompanionCommandResult.Unconfirmed
				: CompanionCommandResult.Failed(failure);
	}
}
