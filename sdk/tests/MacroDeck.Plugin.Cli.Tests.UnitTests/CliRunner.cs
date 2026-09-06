using MacroDeck.Plugin.Cli.Building;
using MacroDeck.Plugin.Cli.Runtime;
using MacroDeck.Plugin.Cli.Scaffolding;
using Spectre.Console.Testing;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary>Drives <see cref="CliEntryPoint.RunAsync" /> with <see cref="StringWriter" />s and no real
/// console or process - shared by every command's own test fixture, matching
/// <see cref="CliEntryPointTests" />'s own private helper.</summary>
internal static class CliRunner
{
	/// <summary>Every plain <see cref="Run(string[])" /> call runs as if launched from a non-interactive
	/// shell with its input seam wired to a reader that throws on any read - the strongest proof a command
	/// never falls back to prompting when it should already have everything it needs (see issue #589's
	/// acceptance scenario e1).</summary>
	private static readonly CliPromptReader _nonInteractivePrompt = new(isInteractive: false,
		readLine: () => throw new InvalidOperationException(
			"A non-interactive CliRunner.Run call attempted to read from the input seam."));

	public static async Task<(string Output, string Error, int ExitCode)> Run(params string[] args)
	{
		var output = new StringWriter();
		var error = new StringWriter();
		var exitCode = await CliEntryPoint.RunAsync(args, output, error, prompt: _nonInteractivePrompt);
		return (output.ToString(), error.ToString(), exitCode);
	}

	public static async Task<(string Output, string Error, int ExitCode)> Run(string[] args,
		CancellationToken cancellationToken)
	{
		var output = new StringWriter();
		var error = new StringWriter();
		var exitCode = await CliEntryPoint.RunAsync(args, output, error, _nonInteractivePrompt, ct: cancellationToken);
		return (output.ToString(), error.ToString(), exitCode);
	}

	/// <summary>As <see cref="Run(string[])" />, but with a fake <see cref="IPluginScaffoldGenerator" /> in
	/// place of the real <c>dotnet new</c> toolchain - the only way any test may exercise <c>new</c>'s
	/// template-management logic without installing a template or touching the network.</summary>
	public static async Task<(string Output, string Error, int ExitCode)> Run(IPluginScaffoldGenerator generator,
		params string[] args)
	{
		var output = new StringWriter();
		var error = new StringWriter();
		var exitCode = await CliEntryPoint.RunAsync(args, output, error, _nonInteractivePrompt, generator);
		return (output.ToString(), error.ToString(), exitCode);
	}

	/// <summary>As <see cref="Run(string[])" />, but with a fake <see cref="IPluginBuildRunner" /> in place
	/// of a real build toolchain - the only way any test may exercise <c>build</c> without spending minutes
	/// publishing per runtime identifier.</summary>
	public static async Task<(string Output, string Error, int ExitCode)> Run(IPluginBuildRunner buildRunner,
		params string[] args)
	{
		var output = new StringWriter();
		var error = new StringWriter();
		var exitCode = await CliEntryPoint.RunAsync(args,
			output,
			error,
			_nonInteractivePrompt,
			buildRunner: buildRunner);
		return (output.ToString(), error.ToString(), exitCode);
	}

	/// <summary>As <see cref="Run(string[])" />, but with a fake <see cref="IHostPairingProbe" /> in place
	/// of a real request to a running host - the only way any test may exercise what <c>run</c> reports
	/// about pairing without a Macro Deck on the machine deciding the outcome.</summary>
	public static async Task<(string Output, string Error, int ExitCode)> Run(IHostPairingProbe pairingProbe,
		params string[] args)
	{
		var output = new StringWriter();
		var error = new StringWriter();
		var exitCode = await CliEntryPoint.RunAsync(args,
			output,
			error,
			_nonInteractivePrompt,
			pairingProbe: pairingProbe);
		return (output.ToString(), error.ToString(), exitCode);
	}

	/// <summary>Drives the wizard with scripted answers, one per line - <paramref name="stdin" /> is split on
	/// <c>\n</c> so a multi-line literal reads naturally at each call site.</summary>
	public static async Task<(string Output, string Error, int ExitCode)> RunInteractive(string stdin,
		params string[] args)
	{
		var lines = new Queue<string>(stdin.Split('\n'));
		var prompt = new CliPromptReader(isInteractive: true, readLine: () => lines.Count > 0 ? lines.Dequeue() : null);

		var output = new StringWriter();
		var error = new StringWriter();
		var exitCode = await CliEntryPoint.RunAsync(args, output, error, prompt: prompt);
		return (output.ToString(), error.ToString(), exitCode);
	}

	/// <summary>As <see cref="RunInteractive(string, string[])" />, but also swapping in a fake
	/// <see cref="IPluginScaffoldGenerator" /> - for a wizard scenario that runs all the way through to a
	/// successful scaffold.</summary>
	public static async Task<(string Output, string Error, int ExitCode)> RunInteractive(string stdin,
		IPluginScaffoldGenerator generator,
		params string[] args)
	{
		var lines = new Queue<string>(stdin.Split('\n'));
		var prompt = new CliPromptReader(isInteractive: true, readLine: () => lines.Count > 0 ? lines.Dequeue() : null);

		var output = new StringWriter();
		var error = new StringWriter();
		var exitCode = await CliEntryPoint.RunAsync(args, output, error, prompt, generator);
		return (output.ToString(), error.ToString(), exitCode);
	}

	/// <summary>Drives the wizard's checkbox prompt through a <see cref="TestConsole" /> instead of the
	/// line-based seam - <paramref name="ansi" /> must already be <c>.Interactive()</c>, with its keys
	/// pushed via <see cref="TestConsoleInput.PushKey(System.ConsoleKey)" /> before the call, and any
	/// non-checkbox prompts (output directory, the final confirmation) still scripted through
	/// <paramref name="stdin" />, one line per call, exactly like <see cref="RunInteractive(string, string[])" />.
	/// The checkbox itself renders into <paramref name="ansi" />'s own output, not the returned
	/// <c>Output</c> - assert against <c>ansi.Output</c> for anything the checkbox prompt wrote.</summary>
	public static async Task<(string Output, string Error, int ExitCode)> RunWithCheckboxPrompt(TestConsole ansi,
		string stdin,
		IPluginScaffoldGenerator generator,
		params string[] args)
		=> await RunWithCheckboxPrompt(ansi, stdin, generator, _ => { }, args);

	/// <summary>As <see cref="RunWithCheckboxPrompt(TestConsole, string, IPluginScaffoldGenerator, string[])" />,
	/// but reporting the <c>noColor</c> the wizard asked the seam for, so a test can assert that
	/// <c>--no-color</c> reaches the console the checkbox is rendered on.</summary>
	public static async Task<(string Output, string Error, int ExitCode)> RunWithCheckboxPrompt(TestConsole ansi,
		string stdin,
		IPluginScaffoldGenerator generator,
		Action<bool> onNoColorRequested,
		params string[] args)
	{
		var lines = new Queue<string>(stdin.Split('\n'));
		var prompt = new CliPromptReader(isInteractive: true,
			readLine: () => lines.Count > 0 ? lines.Dequeue() : null,
			interactiveConsole: (_, noColor) =>
			{
				onNoColorRequested(noColor);
				return ansi;
			});

		var output = new StringWriter();
		var error = new StringWriter();
		var exitCode = await CliEntryPoint.RunAsync(args, output, error, prompt, generator);
		return (output.ToString(), error.ToString(), exitCode);
	}
}
