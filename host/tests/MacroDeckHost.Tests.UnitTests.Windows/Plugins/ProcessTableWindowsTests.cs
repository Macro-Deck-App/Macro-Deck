using System.Diagnostics;
using System.Globalization;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Infrastructure.Plugins.Jobs;
using MacroDeckHost.Tests.UnitTests.Plugins.Runtime;

namespace MacroDeckHost.Tests.UnitTests.Windows.Plugins;

[Platform("Win")]
[TestFixture]
internal sealed class ProcessTableWindowsTests
{
	private readonly List<IPluginProcess> _started = [];

	[TearDown]
	public void TearDown()
	{
		foreach (var process in _started)
		{
			try
			{
				process.KillTree().GetAwaiter().GetResult();
			}
			catch (Exception)
			{
			}

			process.Dispose();
		}

		_started.Clear();
	}

	private static ProcessTable CreateProcessTable() => new(Serilog.Core.Logger.None);

	private IPluginProcess StartStub(params string[] arguments)
	{
		var launcher = new PluginProcessLauncher(new PluginProcessJobFactory(Serilog.Core.Logger.None),
			Serilog.Core.Logger.None);

		var process = launcher.Start(new PluginProcessStartRequest
		{
			ExecutablePath = PluginStubLocator.FindExecutable(),
			WorkingDirectory = Path.GetTempPath(),
			Arguments = arguments,
			Environment = new Dictionary<string, string?>(),
			BootstrapOutputMaxLines = 10,
			BootstrapOutputMaxBytes = 4096
		});

		_started.Add(process);
		return process;
	}

	[Test]
	[CancelAfter(60_000)]
	public async Task A_pid_from_a_previous_session_can_still_be_killed_tree_and_all()
	{
		var process = StartStub("--spawn-child", "--sleep", "120000");
		var childPid = await WaitForChildPid(process);
		var parentPid = process.Id;

		await CreateProcessTable().KillTree(parentPid);

		await WaitUntilGone(parentPid, TimeSpan.FromSeconds(10));
		await WaitUntilGone(childPid, TimeSpan.FromSeconds(10));

		Assert.Multiple(() =>
		{
			Assert.That(IsRunning(parentPid), Is.False, "the plugin process itself must be gone");
			Assert.That(IsRunning(childPid), Is.False, "the spawned child must be gone too");
		});
	}

	[Test]
	[CancelAfter(60_000)]
	public async Task Start_times_are_stable_per_process_and_order_two_processes_apart()
	{
		var first = StartStub("--sleep", "120000");
		await Task.Delay(TimeSpan.FromMilliseconds(1500));
		var second = StartStub("--sleep", "120000");

		var table = CreateProcessTable();
		var firstReading = table.TryGet(first.Id);
		var firstAgain = table.TryGet(first.Id);
		var secondReading = table.TryGet(second.Id);

		Assert.That(firstReading, Is.Not.Null);
		Assert.That(secondReading, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(firstAgain!.Value.StartedAt, Is.EqualTo(firstReading!.Value.StartedAt));
			Assert.That(secondReading!.Value.StartedAt - firstReading!.Value.StartedAt,
				Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(1)),
				"a process table that reported the current time would make every start time comparison vacuous");
		});
	}

	[Test]
	[CancelAfter(60_000)]
	public void The_launchers_start_time_and_the_process_tables_agree()
	{
		var process = StartStub("--sleep", "120000");

		var running = CreateProcessTable().TryGet(process.Id);

		Assert.That(running, Is.Not.Null);
		Assert.That(running!.Value.StartedAt,
			Is.EqualTo(process.StartedAt).Within(TimeSpan.FromSeconds(2)),
			"the journal records the launcher's reading and the reaper compares the table's");
	}

	[Test]
	[CancelAfter(60_000)]
	public async Task Unknown_and_nonsensical_pids_are_neither_reported_nor_killed()
	{
		var bystander = StartStub("--sleep", "120000");
		var exited = StartStub("--exit", "0");
		await exited.Exited.WaitAsync(TimeSpan.FromSeconds(15));

		var table = CreateProcessTable();

		Assert.Multiple(() =>
		{
			Assert.That(table.TryGet(int.MaxValue), Is.Null);
			Assert.That(table.TryGet(0), Is.Null);
			Assert.That(table.TryGet(-1), Is.Null);
			Assert.That(table.TryGet(exited.Id), Is.Null);
		});

		Assert.DoesNotThrowAsync(async () =>
		{
			await table.KillTree(exited.Id);
			await table.KillTree(0);
			await table.KillTree(-1);
		});

		await Task.Delay(TimeSpan.FromSeconds(1));
		Assert.That(bystander.HasExited, Is.False, "an unrelated process must survive a nonpositive pid kill");
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
