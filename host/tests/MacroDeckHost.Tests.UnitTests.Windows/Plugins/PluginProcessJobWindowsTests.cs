using System.Diagnostics;
using System.Globalization;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Infrastructure.Plugins.Jobs;
using MacroDeckHost.Tests.UnitTests.Plugins.Runtime;

namespace MacroDeckHost.Tests.UnitTests.Windows.Plugins;

[Platform("Win")]
[TestFixture]
internal sealed class PluginProcessJobWindowsTests
{
	private static IPluginProcess StartStub()
	{
		var launcher = new PluginProcessLauncher(new PluginProcessJobFactory(Serilog.Core.Logger.None),
			Serilog.Core.Logger.None);

		return launcher.Start(new PluginProcessStartRequest
		{
			ExecutablePath = PluginStubLocator.FindExecutable(),
			WorkingDirectory = Path.GetTempPath(),
			Arguments = ["--spawn-child", "--sleep", "120000"],
			Environment = new Dictionary<string, string?>(),
			BootstrapOutputMaxLines = 10,
			BootstrapOutputMaxBytes = 4096
		});
	}

	[Test]
	[CancelAfter(60_000)]
	public async Task Disposing_the_plugin_process_takes_its_whole_tree_with_it()
	{
		var process = StartStub();
		var childPid = await WaitForChildPid(process);
		var parentPid = process.Id;

		process.Dispose();

		await WaitUntilGone(parentPid, TimeSpan.FromSeconds(10));
		await WaitUntilGone(childPid, TimeSpan.FromSeconds(10));

		Assert.Multiple(() =>
		{
			Assert.That(IsRunning(parentPid), Is.False);
			Assert.That(IsRunning(childPid), Is.False);
		});
	}

	[Test]
	[CancelAfter(60_000)]
	public async Task KillTree_still_ends_the_tree_when_a_job_is_present()
	{
		using var process = StartStub();
		var childPid = await WaitForChildPid(process);
		var parentPid = process.Id;

		await process.KillTree();

		await WaitUntilGone(parentPid, TimeSpan.FromSeconds(10));
		await WaitUntilGone(childPid, TimeSpan.FromSeconds(10));

		Assert.Multiple(() =>
		{
			Assert.That(IsRunning(parentPid), Is.False);
			Assert.That(IsRunning(childPid), Is.False);
		});
	}

	private static async Task<int> WaitForChildPid(IPluginProcess process)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
		while (DateTime.UtcNow < deadline)
		{
			var line = process.BootstrapOutput.FirstOrDefault(l =>
				l.StartsWith("CHILD_PID=", StringComparison.Ordinal));
			if (line is not null)
			{
				return int.Parse(line["CHILD_PID=".Length..], CultureInfo.InvariantCulture);
			}

			await Task.Delay(50);
		}

		throw new TimeoutException("The stub never reported its spawned child's PID.");
	}

	private static async Task WaitUntilGone(int pid, TimeSpan timeout)
	{
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline && IsRunning(pid))
		{
			await Task.Delay(100);
		}
	}

	private static bool IsRunning(int pid)
	{
		try
		{
			using var process = Process.GetProcessById(pid);
			return !process.HasExited;
		}
		catch (ArgumentException)
		{
			return false;
		}
	}
}
