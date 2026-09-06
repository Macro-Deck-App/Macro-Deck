using System.CommandLine;
using System.CommandLine.Help;
using MacroDeck.Plugin.Cli.Building;
using MacroDeck.Plugin.Cli.Commands;
using MacroDeck.Plugin.Cli.Runtime;
using MacroDeck.Plugin.Cli.Scaffolding;

namespace MacroDeck.Plugin.Cli;

/// <summary>
/// The whole of what used to be <c>Program.cs</c>'s top-level statements, moved here so it is reachable
/// from tests: <c>Program.cs</c> itself is just <see cref="RunAsync" /> plus the process-wide
/// <see cref="ShutdownSignal" /> wiring.
/// </summary>
internal static class CliEntryPoint
{
	public static Command CreateRootCommand(CliPromptReader? prompt = null,
		IPluginScaffoldGenerator? scaffoldGenerator = null,
		IPluginBuildRunner? buildRunner = null,
		IHostPairingProbe? pairingProbe = null)
	{
		// A plain Command, not RootCommand: RootCommand's usage line takes its name from
		// RootCommand.ExecutableName, which is the entry assembly's name (MacroDeck.Plugin.Cli) even when
		// the tool is installed and invoked as macrodeck-plugin - renaming the assembly is blocked by
		// MacroDeck.Plugin.Testing's InternalsVisibleTo. A Command constructed with an explicit name renders
		// that name instead, for both the root and every subcommand's own usage line.
		var rootCommand = new Command("macrodeck-plugin",
			"macrodeck-plugin: build, validate, inspect, pack, run, test, sign and verify Macro Deck plugins " +
			"without a running host.");

		// Only RootCommand adds --help/--version for you; a plain Command needs them added explicitly.
		rootCommand.Add(new HelpOption());
		rootCommand.Add(new VersionOption());

		rootCommand.Add(GlobalOptions.VerbosityOption);
		rootCommand.Add(GlobalOptions.NoColorOption);
		rootCommand.Add(NewCommand.Create(prompt, scaffoldGenerator));
		rootCommand.Add(BuildCommand.Create(buildRunner));
		rootCommand.Add(ValidateCommand.Create());
		rootCommand.Add(InspectCommand.Create());
		rootCommand.Add(PackCommand.Create());
		rootCommand.Add(RunCommand.Create(pairingProbe));
		rootCommand.Add(TestCommand.Create());
		rootCommand.Add(KeygenCommand.Create());
		rootCommand.Add(SignCommand.Create());
		rootCommand.Add(VerifyCommand.Create());

		return rootCommand;
	}

	public static async Task<int> RunAsync(string[] args,
		TextWriter output,
		TextWriter error,
		CliPromptReader? prompt = null,
		IPluginScaffoldGenerator? scaffoldGenerator = null,
		IPluginBuildRunner? buildRunner = null,
		IHostPairingProbe? pairingProbe = null,
		CancellationToken ct = default)
	{
		var rootCommand = CreateRootCommand(prompt, scaffoldGenerator, buildRunner, pairingProbe);
		var parseResult = rootCommand.Parse(args);
		var console = ConsoleFactory.From(parseResult, output, error);

		if (args.Length == 0)
		{
			// Identical output to `--help` itself: reusing the real help action here, rather than writing a
			// second usage renderer, so the two can never drift.
			var helpInvocationConfiguration = new InvocationConfiguration { Output = output, Error = error };
			await rootCommand.Parse("--help").InvokeAsync(helpInvocationConfiguration, ct).ConfigureAwait(false);
			output.WriteLine("Run 'macrodeck-plugin <command> --help' for a command's options.");
			return ExitCode.UsageError;
		}

		// A parse failure (a bad flag, a missing required value, one of GlobalOptions/*Command's own
		// CustomParser calls reporting via ArgumentResult.AddError) is a usage error - ExitCode.UsageError - for
		// every command uniformly, checked here rather than left to InvokeAsync's own default handling so this
		// tool never depends on guessing which exit code that default happens to use. --help and --version
		// produce no parse errors (they redirect ParseResult.Action instead), so this never intercepts them.
		if (parseResult.Errors.Count > 0)
		{
			// The unmatched tokens, not the raw args: a root-level option's own value is a bare word too, so
			// scanning args would report '--verbosity quiet zzz' as the unknown command 'quiet'. A token the
			// parser could not attach to anything, while no subcommand was selected, is the typo.
			var firstToken = parseResult.CommandResult.Command == rootCommand
				? parseResult.UnmatchedTokens.FirstOrDefault(token => !token.StartsWith('-'))
				: null;

			if (firstToken is not null)
			{
				// The unknown-command case gets exactly one line, not the four unrecognized-token parse
				// errors System.CommandLine would otherwise report for the same input (the typo itself, plus
				// every option that followed it, each reported as its own unmatched token).
				var suggestion = CommandSuggestions.For(firstToken, rootCommand.Subcommands.Select(c => c.Name));

				console.WriteError("unknown-command",
					suggestion is not null
						? $"'{firstToken}' is not a macrodeck-plugin command. Did you mean '{suggestion}'?"
						: $"'{firstToken}' is not a macrodeck-plugin command. Run 'macrodeck-plugin --help' to see every " +
						"command.");

				return ExitCode.UsageError;
			}

			foreach (var parseError in parseResult.Errors)
			{
				console.WriteError("usage-error", parseError.Message);
			}

			return ExitCode.UsageError;
		}

		var invocationConfiguration = new InvocationConfiguration
		{
			Output = output,
			Error = error,

			// This tool maps every failure to one of the documented exit codes itself; the default handler would
			// print its own formatting and pick its own code for an exception a command's action did not catch.
			EnableDefaultExceptionHandler = false,

			// No forced process termination on cancellation: run's own shutdown sequence can legitimately take up
			// to the manifest's 60-second maximum grace period, and every other command bounds its own work
			// well inside that regardless (the conformance suite's own per-check and overall watchdogs, in
			// particular).
			ProcessTerminationTimeout = null
		};

		try
		{
			return await parseResult.InvokeAsync(invocationConfiguration, ct).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			return ExitCode.Cancelled;
		}
		catch (Exception ex)
		{
			console.WriteError("internal-error", "macrodeck-plugin hit an error it did not anticipate.", ex.ToString());
			return ExitCode.InternalError;
		}
	}
}
