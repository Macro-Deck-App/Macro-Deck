using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Lifecycle;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Tests.UnitTests.Services;

public class ApplicationRestartServiceTests
{
	private sealed class FakeLifetime : IHostApplicationLifetime
	{
		private readonly TaskCompletionSource _stopped = new();
		private int _stopCount;

		public Task Stopped => _stopped.Task;

		public int StopCount => Volatile.Read(ref _stopCount);

		public CancellationToken ApplicationStarted => CancellationToken.None;
		public CancellationToken ApplicationStopping => CancellationToken.None;
		public CancellationToken ApplicationStopped => CancellationToken.None;

		public void StopApplication()
		{
			Interlocked.Increment(ref _stopCount);
			_stopped.TrySetResult();
		}
	}

	private static ApplicationRestartService CreateService(FakeLifetime lifetime,
		string? shellExecutable,
		TimeSpan? shutdownDelay = null)
		=> new(lifetime, shellExecutable, shutdownDelay ?? TimeSpan.Zero);

	[Test]
	public async Task Restarting_stops_the_application_and_marks_the_exit_code()
	{
		var lifetime = new FakeLifetime();
		var service = CreateService(lifetime, "/Applications/Macro Deck.app");

		var result = service.Request("network-port");
		await lifetime.Stopped.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(service.Availability.Supported, Is.True);
			Assert.That(service.RestartRequested, Is.True);
		});
	}

	[Test]
	public async Task The_shutdown_is_deferred_so_the_response_can_be_written()
	{
		var lifetime = new FakeLifetime();
		var service = CreateService(lifetime,
			"/Applications/Macro Deck.app",
			TimeSpan.FromMilliseconds(250));

		service.Request("network-port");
		var stoppedImmediately = lifetime.StopCount;
		await lifetime.Stopped.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(stoppedImmediately, Is.Zero);
			Assert.That(lifetime.StopCount, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Restarting_without_a_desktop_shell_is_refused()
	{
		var lifetime = new FakeLifetime();
		var service = CreateService(lifetime, null);

		var result = service.Request("network-port");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(RestartError.NotSupported));
			Assert.That(result.ErrorMessage, Is.Not.Empty);
			Assert.That(service.Availability.Supported, Is.False);
			Assert.That(service.Availability.Reason, Is.Not.Empty);
			Assert.That(service.RestartRequested, Is.False);
		});

		var stopped = await Task.WhenAny(lifetime.Stopped, Task.Delay(TimeSpan.FromMilliseconds(100)));
		Assert.That(stopped, Is.Not.SameAs(lifetime.Stopped));
	}

	[Test]
	public async Task Requesting_a_restart_twice_stops_the_application_once()
	{
		var lifetime = new FakeLifetime();
		var service = CreateService(lifetime, "/Applications/Macro Deck.app");

		var first = service.Request("network-port");
		var second = service.Request("network-port");
		await lifetime.Stopped.WaitAsync(TimeSpan.FromSeconds(5));
		// The second request must not schedule a shutdown of its own.
		await Task.Delay(TimeSpan.FromMilliseconds(50));

		Assert.Multiple(() =>
		{
			Assert.That(first.Success, Is.True);
			Assert.That(second.Success, Is.True);
			Assert.That(service.RestartRequested, Is.True);
			Assert.That(lifetime.StopCount, Is.EqualTo(1));
		});
	}
}
