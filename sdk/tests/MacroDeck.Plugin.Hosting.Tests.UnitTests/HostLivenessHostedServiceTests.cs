using System.Diagnostics;
using System.Globalization;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

/// <summary>
/// <see cref="HostLivenessHostedService" /> against a plugin built the way <see cref="RegistrationModeTests" />
/// builds one - <c>MacroDeckPlugin.CreatePlugin()</c> plus <c>builder.Configuration[...]</c> - so the
/// resolved <see cref="PluginRegistrationModeAccessor" /> and <see cref="PluginHostOptions" /> are the
/// real, bound values rather than hand-built ones. The service itself is then driven directly, the same
/// way <c>ShutdownRegressionTests</c> drives <c>IntegrationLifecycleHostedService</c> - no socket, no
/// Kestrel, just the hosted service's own <see cref="IHostedService.StartAsync" /> against a
/// <see cref="ManualTimeProvider" />.
///
/// <para>
/// The only observable asserted anywhere here is whether <c>IHostApplicationLifetime.ApplicationStopping</c>
/// has been signalled - never the poll interval, the timer type, or a log message.
/// </para>
/// </summary>
[TestFixture]
public class HostLivenessHostedServiceTests
{
	private static readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(5);

	private ManualTimeProvider _time = null!;
	private HostLivenessHostedService _service = null!;
	private IHostApplicationLifetime _lifetime = null!;
	private PluginApplication _plugin = null!;

	[TearDown]
	public async Task TearDown()
	{
		await _service.StopAsync(CancellationToken.None);
		_service.Dispose();
		await _plugin.DisposeAsync();
	}

	private void CreateService(PluginHostBuilder builder)
	{
		_time = new ManualTimeProvider();
		_plugin = builder.Build();

		var options = _plugin.Services.GetRequiredService<IOptions<PluginHostOptions>>();
		var mode = _plugin.Services.GetRequiredService<PluginRegistrationModeAccessor>();
		_lifetime = _plugin.Services.GetRequiredService<IHostApplicationLifetime>();

		_service = new HostLivenessHostedService(options, mode, _lifetime, _time, Serilog.Core.Logger.None);
	}

	private static PluginHostBuilder ManagedBuilder()
	{
		var builder = MacroDeckPlugin.CreatePlugin();
		builder.Configuration["MacroDeck:Plugin:Id"] = "com.example.test";
		builder.Configuration["MacroDeck:Plugin:Secret"] = "s";
		return builder;
	}

	private static PluginHostBuilder SelfRegisteringBuilder() => MacroDeckPlugin.CreatePlugin();

	private Task UntilStoppedAsync()
		=> Wait.UntilAsync(() =>
		{
			_time.Advance(_pollInterval);
			return _lifetime.ApplicationStopping.IsCancellationRequested;
		});

	private async Task AssertNeverStopsAsync()
	{
		// Twice the poll interval, each advance followed by a real yield so the background loop's
		// PeriodicTimer continuation - scheduled on the thread pool - has a chance to run before the
		// next advance.
		for (var i = 0; i < 4; i++)
		{
			_time.Advance(_pollInterval);
			await Task.Delay(20);
		}

		Assert.That(_lifetime.ApplicationStopping.IsCancellationRequested, Is.False);
	}

	/// <summary>Starts and waits for a real, trivial child process, then hands back its now-dead pid - the
	/// genuine OS boundary for "a process id that is not alive", rather than a guessed number.</summary>
	private static string DeadProcessId()
	{
		var startInfo = OperatingSystem.IsWindows()
			? new ProcessStartInfo("cmd.exe") { UseShellExecute = false }
			: new ProcessStartInfo("/bin/sh") { UseShellExecute = false };

		startInfo.ArgumentList.Add(OperatingSystem.IsWindows() ? "/c" : "-c");
		startInfo.ArgumentList.Add("exit 0");

		using var process = Process.Start(startInfo)!;
		process.WaitForExit();
		return process.Id.ToString(CultureInfo.InvariantCulture);
	}

	private static string CurrentProcessId() => Environment.ProcessId.ToString(CultureInfo.InvariantCulture);

	private static string CurrentProcessStartedAt() =>
		Process.GetCurrentProcess().StartTime.ToUniversalTime().ToString("O");

	[Test]
	public async Task A_managed_plugin_stops_once_the_watched_host_process_is_gone()
	{
		var builder = ManagedBuilder();
		builder.Configuration["MacroDeck:Plugin:HostProcessId"] = DeadProcessId();
		builder.Configuration["MacroDeck:Plugin:HostStartedAt"] = DateTimeOffset.UtcNow.ToString("O");
		CreateService(builder);

		await _service.StartAsync(CancellationToken.None);
		await UntilStoppedAsync();

		Assert.That(_lifetime.ApplicationStopping.IsCancellationRequested, Is.True);
	}

	[Test]
	public async Task A_managed_plugin_does_not_stop_while_the_watched_host_process_is_alive()
	{
		var builder = ManagedBuilder();
		builder.Configuration["MacroDeck:Plugin:HostProcessId"] = CurrentProcessId();
		builder.Configuration["MacroDeck:Plugin:HostStartedAt"] = CurrentProcessStartedAt();
		CreateService(builder);

		await _service.StartAsync(CancellationToken.None);
		await AssertNeverStopsAsync();
	}

	[Test]
	public async Task A_self_registering_plugin_does_not_stop_when_the_watched_host_process_is_gone()
	{
		var builder = SelfRegisteringBuilder();
		builder.Configuration["MacroDeck:Plugin:HostProcessId"] = DeadProcessId();
		builder.Configuration["MacroDeck:Plugin:HostStartedAt"] = DateTimeOffset.UtcNow.ToString("O");
		CreateService(builder);

		await _service.StartAsync(CancellationToken.None);
		await AssertNeverStopsAsync();
	}

	[Test]
	public async Task ExitWhenHostProcessDies_false_keeps_a_managed_plugin_with_a_dead_host_running()
	{
		var builder = ManagedBuilder();
		builder.Configuration["MacroDeck:Plugin:ExitWhenHostProcessDies"] = "false";
		builder.Configuration["MacroDeck:Plugin:HostProcessId"] = DeadProcessId();
		builder.Configuration["MacroDeck:Plugin:HostStartedAt"] = DateTimeOffset.UtcNow.ToString("O");
		CreateService(builder);

		await _service.StartAsync(CancellationToken.None);
		await AssertNeverStopsAsync();
	}

	[Test]
	public async Task ExitWhenHostProcessDies_true_stops_a_self_registering_plugin_with_a_dead_host()
	{
		var builder = SelfRegisteringBuilder();
		builder.Configuration["MacroDeck:Plugin:ExitWhenHostProcessDies"] = "true";
		builder.Configuration["MacroDeck:Plugin:HostProcessId"] = DeadProcessId();
		builder.Configuration["MacroDeck:Plugin:HostStartedAt"] = DateTimeOffset.UtcNow.ToString("O");
		CreateService(builder);

		await _service.StartAsync(CancellationToken.None);
		await UntilStoppedAsync();

		Assert.That(_lifetime.ApplicationStopping.IsCancellationRequested, Is.True);
	}

	[Test]
	public async Task A_managed_plugin_with_neither_environment_variable_runs_normally()
	{
		var builder = ManagedBuilder();
		CreateService(builder);

		Assert.DoesNotThrowAsync(() => _service.StartAsync(CancellationToken.None));
		await AssertNeverStopsAsync();
	}

	[TestCase("")]
	[TestCase("abc")]
	[TestCase("0")]
	[TestCase("-1")]
	[TestCase("99999999999999")]
	public async Task A_malformed_host_process_id_means_no_watch(string hostProcessId)
	{
		var builder = ManagedBuilder();
		builder.Configuration["MacroDeck:Plugin:HostProcessId"] = hostProcessId;
		builder.Configuration["MacroDeck:Plugin:HostStartedAt"] = DateTimeOffset.UtcNow.ToString("O");

		Assert.DoesNotThrow(() => CreateService(builder));

		await _service.StartAsync(CancellationToken.None);
		await AssertNeverStopsAsync();
	}

	[Test]
	public async Task A_pid_that_matches_but_a_start_time_that_does_not_is_treated_as_the_host_being_gone()
	{
		var builder = ManagedBuilder();
		builder.Configuration["MacroDeck:Plugin:HostProcessId"] = CurrentProcessId();
		builder.Configuration["MacroDeck:Plugin:HostStartedAt"] =
			DateTimeOffset.Parse(CurrentProcessStartedAt(), CultureInfo.InvariantCulture).AddHours(1).ToString("O");
		CreateService(builder);

		await _service.StartAsync(CancellationToken.None);
		await UntilStoppedAsync();

		Assert.That(_lifetime.ApplicationStopping.IsCancellationRequested, Is.True);
	}

	[Test]
	public void The_default_is_additive_with_no_behaviour_change_for_the_old_SDK_shape()
	{
		Assert.That(new PluginHostOptions().ExitWhenHostProcessDies, Is.Null);
	}
}
