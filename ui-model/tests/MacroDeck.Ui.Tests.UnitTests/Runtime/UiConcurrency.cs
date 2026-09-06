using System.Collections.Concurrent;

namespace MacroDeck.Ui.Tests.UnitTests.Runtime;

/// <summary>
/// The harness every #830 concurrency test is built on. Two rules it exists to enforce: a worker is released
/// from a barrier rather than from a sleep, so the race is provoked rather than hoped for; and every wait is
/// bounded and asserted, so a runtime that deadlocks fails a named assertion instead of hanging the run.
/// </summary>
internal static class UiConcurrency
{
	/// <summary>How long a barrier, handshake or join may take before it is reported as a deadlock. Generous
	/// on purpose - it only ever costs time on the failure path, and a saturated machine must not fail it.
	/// </summary>
	internal static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

	/// <summary>Starts one thread per body, releases them all from one barrier and joins them under a bounded
	/// wait. A fault in a body lands in <paramref name="failures" /> instead of vanishing on a pool thread.
	/// </summary>
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
				Name = $"ui-concurrency-{index}",
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
				$"'{thread.Name}' never finished - the runtime deadlocked");
		}
	}

	/// <summary>Runs <paramref name="body" /> on <paramref name="workers" /> threads, each told which one it
	/// is.</summary>
	internal static void RunConcurrently(ConcurrentBag<Exception> failures, int workers, Action<int> body)
	{
		var bodies = new Action[workers];

		for (var index = 0; index < workers; index++)
		{
			var worker = index;
			bodies[index] = () => body(worker);
		}

		RunConcurrently(failures, bodies);
	}

	internal static void AssertNoWorkerFaulted(ConcurrentBag<Exception> failures)
		=> Assert.That(failures,
			Is.Empty,
			failures.IsEmpty
				? string.Empty
				: string.Join(Environment.NewLine, failures.Select(failure => failure.ToString())));
}
