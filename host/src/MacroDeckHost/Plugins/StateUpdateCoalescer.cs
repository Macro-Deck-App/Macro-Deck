using System.Collections.Concurrent;

namespace MacroDeckHost.Plugins;

internal sealed class StateUpdateCoalescer
{
	private readonly ConcurrentDictionary<string, CoalescedRun> _runs = new(StringComparer.Ordinal);

	public async Task RunAsync(string key, Func<Task> run)
	{
		var state = _runs.GetOrAdd(key, static _ => new CoalescedRun());

		lock (state.Gate)
		{
			if (state.Running)
			{
				state.Pending = true;
				return;
			}

			state.Running = true;
		}

		try
		{
			while (true)
			{
				await run().ConfigureAwait(false);

				lock (state.Gate)
				{
					if (!state.Pending)
					{
						state.Running = false;
						return;
					}

					state.Pending = false;
				}
			}
		}
		catch
		{
			lock (state.Gate)
			{
				state.Running = false;
				state.Pending = false;
			}

			throw;
		}
	}

	private sealed class CoalescedRun
	{
		public readonly Lock Gate = new();
		public bool Running;
		public bool Pending;
	}
}
