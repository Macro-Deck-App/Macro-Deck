using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Infrastructure.Plugins.Jobs;
using MacroDeckHost.Tests.UnitTests.Plugins.Runtime;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Windows.Plugins;

[Platform("Win")]
[TestFixture]
internal sealed class PluginVersionDirectoryLockWindowsTests
{
	private const int SharingViolation = unchecked((int)0x80070020);

	private TestPaths _paths = null!;

	[SetUp]
	public void SetUp() => _paths = new TestPaths();

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	[Test]
	[CancelAfter(30_000)]
	public async Task A_running_plugin_locks_its_version_directory_until_it_has_exited()
	{
		var versionDirectory = Path.Combine(_paths.BaseDirectory, "1.0.0");
		var displacedDirectory = Path.Combine(_paths.BaseDirectory, "displaced");
		Directory.CreateDirectory(versionDirectory);

		var launcher = new PluginProcessLauncher(new PluginProcessJobFactory(Serilog.Core.Logger.None),
			Serilog.Core.Logger.None);
		using var process = launcher.Start(new PluginProcessStartRequest
		{
			ExecutablePath = PluginStubLocator.FindExecutable(),
			WorkingDirectory = versionDirectory,
			Arguments = ["--print-env", "STUB_READY", "--sleep", "120000"],
			Environment = new Dictionary<string, string?> { ["STUB_READY"] = "ready" },
			BootstrapOutputMaxLines = 10,
			BootstrapOutputMaxBytes = 4096
		});
		await WaitForReady(process);

		var whileRunning = Assert.Throws<IOException>(() => Directory.Move(versionDirectory, displacedDirectory));

		await process.KillTree();
		await process.Exited.WaitAsync(TimeSpan.FromSeconds(15));

		Assert.Multiple(() =>
		{
			Assert.That(whileRunning!.HResult, Is.EqualTo(SharingViolation));
			Assert.DoesNotThrow(() => Directory.Move(versionDirectory, displacedDirectory));
		});
	}

	private static async Task WaitForReady(IPluginProcess process)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
		while (DateTime.UtcNow < deadline)
		{
			if (process.BootstrapOutput.Contains("ready"))
			{
				return;
			}

			await Task.Delay(50);
		}

		throw new TimeoutException("The stub never reported that it was running.");
	}
}
