using System.Diagnostics;
using System.Globalization;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Infrastructure.Plugins.Jobs;
using MacroDeckHost.Tests.UnitTests.Plugins.Runtime;

namespace MacroDeckHost.Tests.UnitTests.MacOS.Plugins;

[Platform("MacOsX")]
[TestFixture]
internal sealed class PluginProcessKillTreeMacOsTests
{
	[Test]
	[CancelAfter(30_000)]
	public async Task KillTree_kills_both_the_process_and_its_spawned_child()
	{
		var launcher = new PluginProcessLauncher(new PluginProcessJobFactory(Serilog.Core.Logger.None),
			Serilog.Core.Logger.None);
		var stubPath = PluginStubLocator.FindExecutable();

		using var process = launcher.Start(new PluginProcessStartRequest
		{
			ExecutablePath = stubPath,
			WorkingDirectory = Path.GetTempPath(),
			Arguments = ["--spawn-child"],
			Environment = new Dictionary<string, string?>(),
			BootstrapOutputMaxLines = 10,
			BootstrapOutputMaxBytes = 4096
		});

		var childPid = await WaitForChildPid(process);
		var parentPid = process.Id;

		await process.KillTree();
		await process.Exited.WaitAsync(TimeSpan.FromSeconds(15));

		await WaitUntilGone(childPid, TimeSpan.FromSeconds(10));

		Assert.Multiple(() =>
		{
			Assert.That(IsRunning(parentPid), Is.False, "the plugin process itself must be gone");
			Assert.That(IsRunning(childPid), Is.False, "the spawned child must be gone too");
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
