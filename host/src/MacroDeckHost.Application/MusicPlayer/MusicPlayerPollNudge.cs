using MacroDeckHost.Application.Integrations;
using MacroDeck.Sdk.MusicPlayer;

namespace MacroDeckHost.Application.MusicPlayer;

public interface IMusicPlayerPollNudge
{
	Task Due { get; }

	void NoteActionExecuted(string integrationId);

	void Rearm();
}

public sealed class MusicPlayerPollNudge : IMusicPlayerPollNudge
{
	private static readonly TimeSpan _defaultDelay = TimeSpan.FromMilliseconds(800);

	private readonly IIntegrationRegistry _integrations;
	private readonly TimeSpan _delay;
	private readonly Lock _sync = new();

	private TaskCompletionSource _due = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private int _scheduled;

	public MusicPlayerPollNudge(IIntegrationRegistry integrations, TimeSpan? delay = null)
	{
		_integrations = integrations;
		_delay = delay ?? _defaultDelay;
	}

	public Task Due
	{
		get
		{
			lock (_sync)
			{
				return _due.Task;
			}
		}
	}

	public void NoteActionExecuted(string integrationId)
	{
		if (!_integrations.Integrations.Any(i => i.Id == integrationId && i is IMusicPlayerProvider))
		{
			return;
		}

		if (Interlocked.CompareExchange(ref _scheduled, 1, 0) != 0)
		{
			return;
		}

		_ = FireAsync();
	}

	public void Rearm()
	{
		lock (_sync)
		{
			if (_due.Task.IsCompleted)
			{
				_due = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			}
		}

		Interlocked.Exchange(ref _scheduled, 0);
	}

	private async Task FireAsync()
	{
		await Task.Delay(_delay);
		TaskCompletionSource due;
		lock (_sync)
		{
			due = _due;
		}

		due.TrySetResult();
	}
}
