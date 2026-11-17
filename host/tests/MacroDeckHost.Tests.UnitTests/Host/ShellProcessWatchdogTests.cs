using System.Globalization;
using MacroDeckHost.Tests.UnitTests.Delegation;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Tests.UnitTests.Host;

[NonParallelizable]
public class ShellProcessWatchdogTests
{
	private const int ShellPid = 4242;
	private static readonly TimeSpan _waitLimit = TimeSpan.FromSeconds(5);

	[Test]
	public async Task The_host_stops_once_the_app_that_started_it_is_gone()
	{
		var currentParent = ShellPid;
		var lifetime = new RecordingLifetime();
		var time = new FakeTimeProvider();
		using var watchdog = new ShellProcessWatchdog(new ShellParent(ShellPid, () => currentParent), lifetime, time);

		await watchdog.StartAsync(CancellationToken.None);
		time.Advance(TimeSpan.FromSeconds(3));
		var stoppedWhileParentAlive = lifetime.StopRequests;

		currentParent = 1;
		time.Advance(TimeSpan.FromSeconds(1));
		await lifetime.Stopped.WaitAsync(_waitLimit);
		time.Advance(TimeSpan.FromSeconds(3));

		await Assert.MultipleAsync(() =>
		{
			Assert.That(stoppedWhileParentAlive, Is.Zero);
			Assert.That(lifetime.StopRequests, Is.EqualTo(1));
			return Task.CompletedTask;
		});
	}

	[Test]
	public async Task A_host_whose_app_died_before_it_started_stops_at_once()
	{
		var lifetime = new RecordingLifetime();
		using var watchdog = new ShellProcessWatchdog(new ShellParent(ShellPid, () => 1),
			lifetime,
			new FakeTimeProvider());

		await watchdog.StartAsync(CancellationToken.None);
		await lifetime.Stopped.WaitAsync(_waitLimit);

		Assert.That(lifetime.StopRequests, Is.EqualTo(1));
	}

	[Test]
	public async Task A_host_not_started_by_the_app_never_stops_itself()
	{
		var lifetime = new RecordingLifetime();
		var time = new FakeTimeProvider();
		using var watchdog = new ShellProcessWatchdog(new ShellParent(null, () => 1), lifetime, time);

		await watchdog.StartAsync(CancellationToken.None);
		time.Advance(TimeSpan.FromSeconds(10));
		await watchdog.ExecuteTask!.WaitAsync(_waitLimit);

		Assert.That(lifetime.StopRequests, Is.Zero);
	}

	[Test]
	public void The_shell_pid_is_not_passed_on_to_processes_the_host_starts()
	{
		Environment.SetEnvironmentVariable(ShellParent.EnvironmentVariable,
			ShellPid.ToString(CultureInfo.InvariantCulture));

		var parent = ShellParent.Take();

		Assert.Multiple(() =>
		{
			Assert.That(Environment.GetEnvironmentVariable(ShellParent.EnvironmentVariable), Is.Null);
			Assert.That(parent.ExpectedProcessId, Is.EqualTo(OperatingSystem.IsWindows() ? null : ShellPid));
		});
	}

	private sealed class RecordingLifetime : IHostApplicationLifetime
	{
		private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private int _stopRequests;

		public int StopRequests => Volatile.Read(ref _stopRequests);

		public Task Stopped => _stopped.Task;

		public CancellationToken ApplicationStarted => CancellationToken.None;

		public CancellationToken ApplicationStopping => CancellationToken.None;

		public CancellationToken ApplicationStopped => CancellationToken.None;

		public void StopApplication()
		{
			Interlocked.Increment(ref _stopRequests);
			_stopped.TrySetResult();
		}
	}
}
