using System.Collections.Concurrent;
using MacroDeckHost.Application.Lifecycle;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Tests.UnitTests.Host;

public class ShutdownDeadlineTests
{
	private static readonly TimeSpan _deadline = TimeSpan.FromMilliseconds(100);
	private static readonly TimeSpan _lastResortDelay = TimeSpan.FromMilliseconds(100);
	private static readonly TimeSpan _patience = TimeSpan.FromSeconds(30);

	private sealed class StubRestartService : IApplicationRestartService
	{
		private int _restartRequested;

		public RestartAvailability Availability => new(true, null);

		public bool RestartRequested => Volatile.Read(ref _restartRequested) == 1;

		public Result<RestartError> Request(string reason)
		{
			Volatile.Write(ref _restartRequested, 1);
			return Result.Ok<RestartError>();
		}
	}

	private sealed class NeverStoppingHostedService : IHostedService
	{
		public Task StartAsync(CancellationToken cancellationToken)
		{
			return Task.CompletedTask;
		}

		public Task StopAsync(CancellationToken cancellationToken)
		{
			return new TaskCompletionSource().Task;
		}
	}

	private sealed class ExitRecorder
	{
		private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

		private readonly TaskCompletionSource<int> _terminated =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public ConcurrentQueue<int> ExitCodes { get; } = new();

		public Task<int> Exited => _exited.Task;

		public Task<int> Terminated => _terminated.Task;

		public void Exit(int exitCode)
		{
			ExitCodes.Enqueue(exitCode);
			_exited.TrySetResult(exitCode);
		}

		public void Terminate(int exitCode)
		{
			_terminated.TrySetResult(exitCode);
		}
	}

	private static IHost CreateHungHost(IApplicationRestartService restart)
	{
		return new HostBuilder()
			.ConfigureServices(services =>
			{
				services.AddSingleton(restart);
				services.AddSingleton<IHostedService, NeverStoppingHostedService>();
			})
			.Build();
	}

	private static void Arm(IHost host, IApplicationRestartService restart, Action<int> exit, Action<int> terminate)
	{
		ShutdownDeadline.Arm(host.Services.GetRequiredService<IHostApplicationLifetime>(),
			restart,
			_deadline,
			_lastResortDelay,
			exit,
			terminate);
	}

	[Test]
	public async Task A_host_whose_stop_never_returns_is_exited_with_a_success_code()
	{
		var restart = new StubRestartService();
		var host = CreateHungHost(restart);
		var recorder = new ExitRecorder();
		Arm(host, restart, recorder.Exit, recorder.Terminate);

		var run = HostRunner.RunAsync(host);
		host.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();

		var exitCode = await recorder.Exited.WaitAsync(_patience);

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.Zero);
			Assert.That(recorder.ExitCodes, Has.Count.EqualTo(1));
			Assert.That(run.IsCompleted, Is.False);
		});
	}

	[Test]
	public async Task A_requested_restart_keeps_its_exit_code_when_the_stop_never_returns()
	{
		var restart = new StubRestartService();
		var host = CreateHungHost(restart);
		var recorder = new ExitRecorder();
		Arm(host, restart, recorder.Exit, recorder.Terminate);

		var run = HostRunner.RunAsync(host);
		restart.Request("update");
		host.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();

		var exitCode = await recorder.Exited.WaitAsync(_patience);

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(HostExitCodes.RestartRequested));
			Assert.That(run.IsCompleted, Is.False);
		});
	}

	[Test]
	public async Task A_stop_callback_that_never_returns_does_not_prevent_the_exit()
	{
		var restart = new StubRestartService();
		var host = CreateHungHost(restart);
		var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
		var recorder = new ExitRecorder();
		Arm(host, restart, recorder.Exit, recorder.Terminate);
		using var release = new ManualResetEventSlim();
		lifetime.ApplicationStopping.Register(() => release.Wait());

		var stop = Task.Run(lifetime.StopApplication);
		try
		{
			var exitCode = await recorder.Exited.WaitAsync(_patience);

			Assert.Multiple(() =>
			{
				Assert.That(exitCode, Is.Zero);
				Assert.That(stop.IsCompleted, Is.False);
			});
		}
		finally
		{
			release.Set();
			await stop.WaitAsync(_patience);
		}
	}

	[Test]
	public async Task An_exit_that_never_returns_is_followed_by_termination_with_the_same_code()
	{
		var restart = new StubRestartService();
		var host = CreateHungHost(restart);
		var recorder = new ExitRecorder();
		using var release = new ManualResetEventSlim();
		Arm(host,
			restart,
			exitCode =>
			{
				recorder.Exit(exitCode);
				release.Wait();
			},
			recorder.Terminate);

		restart.Request("update");
		host.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
		try
		{
			var terminatedWith = await recorder.Terminated.WaitAsync(_patience);

			Assert.That(terminatedWith, Is.EqualTo(HostExitCodes.RestartRequested));
		}
		finally
		{
			release.Set();
		}
	}

	[Test]
	public async Task A_host_that_is_not_stopping_is_left_alone()
	{
		var restart = new StubRestartService();
		using var host = CreateHungHost(restart);
		var recorder = new ExitRecorder();
		Arm(host, restart, recorder.Exit, recorder.Terminate);

		await Task.Delay(_deadline + _lastResortDelay + TimeSpan.FromMilliseconds(500));

		Assert.Multiple(() =>
		{
			Assert.That(recorder.Exited.IsCompleted, Is.False);
			Assert.That(recorder.Terminated.IsCompleted, Is.False);
		});
	}
}
