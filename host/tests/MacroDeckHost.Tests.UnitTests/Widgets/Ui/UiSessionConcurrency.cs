using System.Collections.Concurrent;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

/// <summary>
/// The harness the issue #830 session tests race through. Workers are released from a barrier rather than
/// from a sleep, every wait is bounded and asserted so a deadlock fails a named assertion instead of hanging
/// the run, and a worker's fault reaches the test thread instead of vanishing on a background thread.
/// </summary>
internal static class UiSessionConcurrency
{
	internal static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

	internal static void RunConcurrently(ConcurrentBag<Exception> failures, params Action[] bodies)
	{
		using var start = new Barrier(bodies.Length);
		var threads = new Thread[bodies.Length];

		for (var index = 0; index < bodies.Length; index++)
		{
			var body = bodies[index];

			threads[index] = new Thread(() =>
			{
				try
				{
					if (!start.SignalAndWait(WaitTimeout))
					{
						throw new TimeoutException("the workers never met at the barrier");
					}

					body();
				}
#pragma warning disable CA1031 // The point of the bag: a worker's fault has to reach the test thread.
				catch (Exception exception)
#pragma warning restore CA1031
				{
					failures.Add(exception);
				}
			})
			{
				IsBackground = true,
				Name = $"session-concurrency-{index}",
			};
		}

		foreach (var thread in threads)
		{
			thread.Start();
		}

		foreach (var thread in threads)
		{
			Assert.That(thread.Join(WaitTimeout),
				Is.True,
				$"'{thread.Name}' never finished - the session deadlocked");
		}
	}

	internal static void AssertNoWorkerFaulted(ConcurrentBag<Exception> failures)
		=> Assert.That(failures,
			Is.Empty,
			failures.IsEmpty
				? string.Empty
				: string.Join(Environment.NewLine, failures.Select(failure => failure.ToString())));
}
