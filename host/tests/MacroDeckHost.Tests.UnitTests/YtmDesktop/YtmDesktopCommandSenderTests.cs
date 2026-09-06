using System.Diagnostics;
using MacroDeckHost.Integrations.YtmDesktop;

namespace MacroDeckHost.Tests.UnitTests.YtmDesktop;

[TestFixture]
internal sealed class YtmDesktopCommandSenderTests
{
	private static readonly TimeSpan _interval = TimeSpan.FromMilliseconds(30);

	[Test]
	public void Enqueue_returns_without_waiting_for_the_send()
	{
		var gate = new TaskCompletionSource();
		using var sender = new YtmDesktopCommandSender((_, _, _) => gate.Task,
			_interval);

		var stopwatch = Stopwatch.StartNew();
		sender.Enqueue("setVolume", 40);
		stopwatch.Stop();

		gate.SetResult();
		Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromMilliseconds(200)));
	}

	[Test]
	public async Task A_burst_collapses_to_the_newest_value()
	{
		var sent = new List<object?>();
		using var sender = new YtmDesktopCommandSender((_, data, _) =>
			{
				lock (sent)
				{
					sent.Add(data);
				}

				return Task.CompletedTask;
			},
			_interval);

		for (var volume = 1; volume <= 20; volume++)
		{
			sender.Enqueue("setVolume", volume);
		}

		await WaitForAsync(() =>
		{
			lock (sent)
			{
				return sent.Contains(20);
			}
		});

		lock (sent)
		{
			Assert.Multiple(() =>
			{
				Assert.That(sent, Has.Count.LessThan(20));
				Assert.That(sent[^1], Is.EqualTo(20));
			});
		}
	}

	[Test]
	public async Task Queued_sends_are_spaced_by_the_interval()
	{
		var times = new List<TimeSpan>();
		var stopwatch = Stopwatch.StartNew();
		using var sender = new YtmDesktopCommandSender((_, _, _) =>
			{
				lock (times)
				{
					times.Add(stopwatch.Elapsed);
				}

				return Task.CompletedTask;
			},
			TimeSpan.FromMilliseconds(120));

		sender.Enqueue("setVolume", 1);
		sender.Enqueue("seekTo", 2);

		await WaitForAsync(() =>
		{
			lock (times)
			{
				return times.Count >= 2;
			}
		});

		lock (times)
		{
			Assert.That(times[1] - times[0], Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(100)));
		}
	}

	[Test]
	public async Task A_direct_send_is_not_delayed_behind_a_queued_burst()
	{
		using var sender = new YtmDesktopCommandSender((_, _, _) => Task.CompletedTask,
			TimeSpan.FromMilliseconds(500));

		sender.Enqueue("setVolume", 1);
		sender.Enqueue("setVolume", 2);

		var stopwatch = Stopwatch.StartNew();
		await sender.SendAsync("next", null, CancellationToken.None);
		stopwatch.Stop();

		Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromMilliseconds(300)));
	}

	[Test]
	public async Task A_rejected_send_does_not_stop_the_queue()
	{
		var sent = new List<string>();
		var fail = true;
		using var sender = new YtmDesktopCommandSender((command, _, _) =>
			{
				if (fail)
				{
					fail = false;
					return Task.FromException(new InvalidOperationException("rejected"));
				}

				lock (sent)
				{
					sent.Add(command);
				}

				return Task.CompletedTask;
			},
			_interval);

		sender.Enqueue("setVolume", 1);
		await Task.Delay(_interval);
		sender.Enqueue("seekTo", 2);

		await WaitForAsync(() =>
		{
			lock (sent)
			{
				return sent.Contains("seekTo");
			}
		});

		lock (sent)
		{
			Assert.That(sent, Does.Contain("seekTo"));
		}
	}

	private static async Task WaitForAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
		while (DateTime.UtcNow < deadline)
		{
			if (condition())
			{
				return;
			}

			await Task.Delay(10);
		}

		Assert.Fail("The expected sends did not arrive in time.");
	}
}
