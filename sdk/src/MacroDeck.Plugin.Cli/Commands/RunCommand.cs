using System.CommandLine;
using MacroDeck.Plugin.Cli.Runtime;
using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Testing;

namespace MacroDeck.Plugin.Cli.Commands;

/// <summary>
/// <c>macrodeck-plugin run</c>: launches a plugin the way the supervisor does - see
/// <see cref="PluginEnvironmentComposer" /> - against the running desktop host by default (discovered
/// through its loopback port file, see <see cref="HostDiscovery" />), an explicit <c>--host-url</c>, or a
/// disposable <see cref="MacroDeckTestHost" /> given <c>--stub-host</c>. Streams the child's output live
/// and, on Ctrl-C, runs the documented supervisor shutdown sequence when it owns the host session to run
/// it against.
/// </summary>
internal static class RunCommand
{
	public static Command Create(IHostPairingProbe? pairingProbe = null)
	{
		var projectOption = new Option<string?>("--project") { Description = "A plugin's .csproj, or its directory." };
		var executableOption = new Option<string?>("--executable")
			{ Description = "An already-built executable or framework-dependent .dll." };
		var artifactOption = new Option<string?>("--artifact") { Description = "A packed .macroDeckPlugin artifact." };

		var hostUrlOption = new Option<string?>("--host-url")
		{
			Description = "A real host's URL. Defaults to the running desktop host, discovered through its " +
				"loopback port file."
		};
		var stubHostOption = new Option<bool>("--stub-host")
			{ Description = "Run against a disposable in-process stub host instead of a real one." };

		var modeOption = CreateModeOption();
		var enrollmentTokenOption = new Option<string?>("--enrollment-token")
		{
			Description = "Self-registering only. Required against a real host unless --pairing is left " +
				"enabled (the default); defaults to a placeholder against --stub-host."
		};
		var pairingOption = new Option<bool>("--pairing")
		{
			Description = "Self-registering only. Whether the plugin may fall back to interactive pairing " +
				"when no --enrollment-token is given. Defaults to true.",
			DefaultValueFactory = _ => true
		};
		var pluginIdOption = new Option<string?>("--plugin-id")
		{
			Description = "Managed only. Defaults to the id declared by the manifest.json next to the launch " +
				"target; a generated development id when no manifest is found."
		};
		var secretOption = new Option<string?>("--secret")
			{ Description = "Managed only. Defaults to a generated secret against --stub-host." };
		var stateDirectoryOption = new Option<string?>("--state-directory")
		{
			Description = "Self-registering only. Where the persisted credential is stored. Defaults to a " +
				"temporary directory."
		};
		var dataDirectoryOption = new Option<string?>("--data-directory")
			{ Description = "Managed only. Defaults to a temporary directory." };
		var instanceIdOption = new Option<string?>("--instance-id")
			{ Description = "Both modes. Defaults to a fresh id." };
		var launchIdOption = new Option<string?>("--launch-id")
			{ Description = "Managed only, diagnostic. Defaults to a fresh id." };
		var listenUrlOption = new Option<string?>("--listen-url")
			{ Description = "Where the plugin listens. Defaults to http://127.0.0.1:0 (an OS-chosen loopback port)." };

		var command = new Command("run", "Launch a plugin against a stub or real host, streaming its output.");
		command.Add(projectOption);
		command.Add(executableOption);
		command.Add(artifactOption);
		command.Add(hostUrlOption);
		command.Add(stubHostOption);
		command.Add(modeOption);
		command.Add(enrollmentTokenOption);
		command.Add(pairingOption);
		command.Add(pluginIdOption);
		command.Add(secretOption);
		command.Add(stateDirectoryOption);
		command.Add(dataDirectoryOption);
		command.Add(instanceIdOption);
		command.Add(launchIdOption);
		command.Add(listenUrlOption);

		command.SetAction(async (parseResult, cancellationToken) =>
		{
			var console = ConsoleFactory.From(parseResult);

			var options = new RunOptions
			{
				Project = parseResult.GetValue(projectOption),
				Executable = parseResult.GetValue(executableOption),
				Artifact = parseResult.GetValue(artifactOption),
				HostUrl = parseResult.GetValue(hostUrlOption),
				StubHost = parseResult.GetValue(stubHostOption),
				Mode = parseResult.GetValue(modeOption),
				EnrollmentToken = parseResult.GetValue(enrollmentTokenOption),
				PairingEnabled = parseResult.GetValue(pairingOption),
				PluginId = parseResult.GetValue(pluginIdOption),
				Secret = parseResult.GetValue(secretOption),
				StateDirectory = parseResult.GetValue(stateDirectoryOption),
				DataDirectory = parseResult.GetValue(dataDirectoryOption),
				InstanceId = parseResult.GetValue(instanceIdOption),
				LaunchId = parseResult.GetValue(launchIdOption),
				ListenUrl = parseResult.GetValue(listenUrlOption)
			};

			return await RunSession.RunAsync(console, options, pairingProbe, cancellationToken).ConfigureAwait(false);
		});

		return command;
	}

	private static Option<PluginRegistrationMode> CreateModeOption()
	{
		var option = new Option<PluginRegistrationMode>("--mode")
		{
			Description = "managed or self-registering. Defaults to self-registering.",
			DefaultValueFactory = _ => PluginRegistrationMode.SelfRegistering
		};

		option.CustomParser = result => CliOptionParsing.ParseToken(result,
			PluginRegistrationMode.SelfRegistering,
			("managed", PluginRegistrationMode.Managed),
			("self-registering", PluginRegistrationMode.SelfRegistering));

		return option;
	}
}

/// <summary>Every <c>run</c> option, parsed and handed to <see cref="RunSession" /> as one value.</summary>
internal sealed record RunOptions
{
	public string? Project { get; init; }

	public string? Executable { get; init; }

	public string? Artifact { get; init; }

	public string? HostUrl { get; init; }

	public bool StubHost { get; init; }

	public required PluginRegistrationMode Mode { get; init; }

	public string? EnrollmentToken { get; init; }

	public bool PairingEnabled { get; init; } = true;

	public string? PluginId { get; init; }

	public string? Secret { get; init; }

	public string? StateDirectory { get; init; }

	public string? DataDirectory { get; init; }

	public string? InstanceId { get; init; }

	public string? LaunchId { get; init; }

	public string? ListenUrl { get; init; }
}
