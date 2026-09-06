using System.Text;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Infrastructure.Plugins.Jobs;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Runtime;

[TestFixture]
internal sealed class PluginProcessLauncherTests
{
	private string _stubPath = null!;
	private string _workingDirectory = null!;

	[SetUp]
	public void SetUp()
	{
		_stubPath = PluginStubLocator.FindExecutable();
		_workingDirectory = Path.GetTempPath();
	}

	private static PluginProcessLauncher CreateLauncher()
		=> new(new PluginProcessJobFactory(Serilog.Core.Logger.None), Serilog.Core.Logger.None);

	private PluginProcessStartRequest Request(IReadOnlyList<string> arguments,
		IReadOnlyDictionary<string, string?>? environment = null,
		int maxLines = 100,
		int maxBytes = 16 * 1024)
		=> new()
		{
			ExecutablePath = _stubPath,
			WorkingDirectory = _workingDirectory,
			Arguments = arguments,
			Environment = environment ?? new Dictionary<string, string?>(),
			BootstrapOutputMaxLines = maxLines,
			BootstrapOutputMaxBytes = maxBytes
		};

	[Test]
	public async Task Environment_variables_actually_reach_the_child()
	{
		var launcher = CreateLauncher();
		var environment = new Dictionary<string, string?>
		{
			["MACRO_DECK_PLUGIN_ID"] = "com.example.plugin",
			["MACRO_DECK_PLUGIN_SECRET"] = "top-secret-value"
		};

		using var process = launcher.Start(Request(
			["--print-env", "MACRO_DECK_PLUGIN_ID", "--print-env", "MACRO_DECK_PLUGIN_SECRET", "--exit", "0"],
			environment));

		var exitCode = await process.Exited.WaitAsync(TimeSpan.FromSeconds(15));

		string[] expected = ["com.example.plugin", "top-secret-value"];
		Assert.That(exitCode, Is.EqualTo(0));
		Assert.That(process.BootstrapOutput, Is.EqualTo(expected));
	}

	[Test]
	public async Task The_exit_code_is_captured()
	{
		var launcher = CreateLauncher();

		using var process = launcher.Start(Request(["--exit", "42"]));

		var exitCode = await process.Exited.WaitAsync(TimeSpan.FromSeconds(15));

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(42));
			Assert.That(process.ExitCode, Is.EqualTo(42));
			Assert.That(process.HasExited, Is.True);
		});
	}

	[Test]
	public async Task Bootstrap_stderr_is_captured_and_bounded_to_the_configured_limits()
	{
		var launcher = CreateLauncher();

		using var process = launcher.Start(Request(["--fail-bootstrap"], maxLines: 20, maxBytes: 4 * 1024));

		var exitCode = await process.Exited.WaitAsync(TimeSpan.FromSeconds(30));

		Assert.That(exitCode, Is.EqualTo(1));
		Assert.That(process.BootstrapOutput, Has.Count.LessThanOrEqualTo(20));

		var totalBytes = process.BootstrapOutput.Sum(line => Encoding.UTF8.GetByteCount(line));
		Assert.That(totalBytes, Is.LessThanOrEqualTo(4 * 1024));
	}

	[Test]
	public async Task Megabytes_of_output_neither_deadlocks_nor_grows_the_buffer_unboundedly()
	{
		var launcher = CreateLauncher();

		using var process = launcher.Start(Request(["--fail-bootstrap"], maxLines: 100, maxBytes: 16 * 1024));

		var exitCode = await process.Exited.WaitAsync(TimeSpan.FromSeconds(30));

		Assert.That(exitCode, Is.EqualTo(1));
		Assert.That(process.BootstrapOutput, Has.Count.LessThanOrEqualTo(100));
	}
}
