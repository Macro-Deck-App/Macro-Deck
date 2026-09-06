using MacroDeckHost.Application.Lifecycle;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Tests.UnitTests.Host;

public class HostRunnerTests
{
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

	private sealed class ThrowingHostedService : IHostedService
	{
		private readonly Exception? _onStart;
		private readonly Exception? _onStop;

		public ThrowingHostedService(Exception? onStart, Exception? onStop)
		{
			_onStart = onStart;
			_onStop = onStop;
		}

		public Task StartAsync(CancellationToken cancellationToken)
			=> _onStart is null ? Task.CompletedTask : Task.FromException(_onStart);

		public Task StopAsync(CancellationToken cancellationToken)
			=> _onStop is null ? Task.CompletedTask : Task.FromException(_onStop);
	}

	private static IHost CreateHost(IApplicationRestartService restart, IHostedService? hostedService = null)
		=> new HostBuilder()
			.ConfigureServices(services =>
			{
				services.AddSingleton(restart);
				if (hostedService is not null)
				{
					services.AddSingleton(hostedService);
				}
			})
			.Build();

	[Test]
	public async Task A_restart_requested_while_running_yields_the_restart_exit_code()
	{
		var restart = new StubRestartService();
		using var host = CreateHost(restart);
		var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

		var run = HostRunner.RunAsync(host);
		restart.Request("network-port");
		lifetime.StopApplication();

		var exitCode = await run.WaitAsync(TimeSpan.FromSeconds(30));

		Assert.That(exitCode, Is.EqualTo(HostExitCodes.RestartRequested));
	}

	[Test]
	public async Task A_shutdown_without_a_restart_request_yields_a_success_exit_code()
	{
		var restart = new StubRestartService();
		using var host = CreateHost(restart);
		var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

		var run = HostRunner.RunAsync(host);
		lifetime.StopApplication();

		var exitCode = await run.WaitAsync(TimeSpan.FromSeconds(30));

		Assert.That(exitCode, Is.Zero);
	}

	[Test]
	public async Task A_restart_survives_a_shutdown_that_fails()
	{
		var restart = new StubRestartService();
		using var host = CreateHost(restart,
			new ThrowingHostedService(null, new InvalidOperationException("stop failed")));
		var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

		var run = HostRunner.RunAsync(host);
		restart.Request("network-port");
		lifetime.StopApplication();

		var exitCode = await run.WaitAsync(TimeSpan.FromSeconds(30));

		Assert.That(exitCode, Is.EqualTo(HostExitCodes.RestartRequested));
	}

	[Test]
	public async Task A_shutdown_that_fails_without_a_restart_request_is_not_reported_as_a_crash()
	{
		var restart = new StubRestartService();
		using var host = CreateHost(restart,
			new ThrowingHostedService(null, new InvalidOperationException("stop failed")));
		var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

		var run = HostRunner.RunAsync(host);
		lifetime.StopApplication();

		var exitCode = await run.WaitAsync(TimeSpan.FromSeconds(30));

		Assert.That(exitCode, Is.Zero);
	}

	[Test]
	public void A_startup_failure_still_propagates()
	{
		var restart = new StubRestartService();
		using var host = CreateHost(restart,
			new ThrowingHostedService(new InvalidOperationException("start failed"), null));

		Assert.That(async () => await HostRunner.RunAsync(host).WaitAsync(TimeSpan.FromSeconds(30)),
			Throws.InstanceOf<InvalidOperationException>());
	}
}
