using MacroDeck.Plugin.Cli.Runtime;
using MacroDeck.Plugin.Protocol.Handshake;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary>
/// What <c>run</c> tells the developer about pairing before the plugin has said anything (issue #752).
/// The old output promised "a pairing prompt will appear" from the options alone, which is a promise the
/// CLI cannot keep while Developer Mode is off - the prompt never appears and nothing says why.
/// </summary>
[TestFixture]
public class RunDeveloperModeTests
{
	private string _target = string.Empty;

	[SetUp]
	public void SetUp()
	{
		// An existing but unlaunchable file: it gets past the subject resolution that a missing path fails
		// at, which is what puts the pairing diagnostic in reach, and then fails to start. What run
		// reports about pairing is decided before the launch either way.
		_target = Path.Combine(Path.GetTempPath(), $"macrodeck-plugin-run-{Guid.NewGuid():N}.dll");
		File.WriteAllText(_target, "not an assembly");
	}

	[TearDown]
	public void TearDown() => File.Delete(_target);

	private static StubProbe Probe(bool? developerModeEnabled, bool answers = true)
		=> new(answers
			? new PluginPairingDescriptor
			{
				Supported = true,
				RequestLifetimeSeconds = 60,
				PollIntervalSeconds = 1,
				DeveloperModeEnabled = developerModeEnabled
			}
			: null);

	[Test]
	public async Task Developer_mode_off_is_reported_instead_of_promising_a_pairing_prompt()
	{
		var (_, error, _) = await CliRunner.Run(Probe(developerModeEnabled: false),
			"run",
			"--executable",
			_target,
			"--host-url",
			"http://127.0.0.1:1");

		Assert.Multiple(() =>
		{
			Assert.That(error, Does.Contain("developer-mode-disabled"));
			Assert.That(error, Does.Not.Contain("pairing-prompt-expected"));
		});
	}

	[Test]
	public async Task Developer_mode_on_promises_no_prompt_the_host_would_refuse()
	{
		var (_, error, _) = await CliRunner.Run(Probe(developerModeEnabled: true),
			"run",
			"--executable",
			_target,
			"--host-url",
			"http://127.0.0.1:1");

		Assert.Multiple(() =>
		{
			Assert.That(error, Does.Not.Contain("developer-mode-disabled"));
			Assert.That(error, Does.Not.Contain("pairing-prompt-expected"));
		});
	}

	[Test]
	public async Task A_host_that_cannot_be_asked_still_gets_the_old_warning()
	{
		// Unreachable, or too old to report the flag. Neither is evidence that Developer Mode is off, so
		// run must say exactly what it said before the probe existed rather than guess.
		var (_, unreportedError, _) = await CliRunner.Run(Probe(developerModeEnabled: null),
			"run",
			"--executable",
			_target,
			"--host-url",
			"http://127.0.0.1:1");
		var (_, unreachableError, _) = await CliRunner.Run(Probe(developerModeEnabled: null, answers: false),
			"run",
			"--executable",
			_target,
			"--host-url",
			"http://127.0.0.1:1");

		Assert.Multiple(() =>
		{
			Assert.That(unreportedError, Does.Contain("pairing-prompt-expected"));
			Assert.That(unreachableError, Does.Contain("pairing-prompt-expected"));
			Assert.That(unreportedError, Does.Not.Contain("developer-mode-disabled"));
			Assert.That(unreachableError, Does.Not.Contain("developer-mode-disabled"));
		});
	}

	private sealed class StubProbe : IHostPairingProbe
	{
		private readonly PluginPairingDescriptor? _descriptor;

		public StubProbe(PluginPairingDescriptor? descriptor) => _descriptor = descriptor;

		public Task<PluginPairingDescriptor?> ProbeAsync(string hostUrl, CancellationToken cancellationToken)
			=> Task.FromResult(_descriptor);
	}
}
