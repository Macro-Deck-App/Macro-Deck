using System.CommandLine;
using MacroDeck.Plugin.Cli.Runtime;

namespace MacroDeck.Plugin.Cli.Commands;

internal enum ConformanceReportFormat
{
	Text,
	Json,
	Markdown
}

/// <summary><c>macrodeck-plugin test</c>: runs the conformance suite (<c>MacroDeck.Plugin.Testing.Conformance</c>)
/// against a project, executable or artifact.</summary>
internal static class TestCommand
{
	public static Command Create()
	{
		var projectOption = new Option<string?>("--project") { Description = "A plugin's .csproj, or its directory." };
		var executableOption = new Option<string?>("--executable")
			{ Description = "An already-built executable or framework-dependent .dll." };
		var artifactOption = new Option<string?>("--artifact") { Description = "A packed .macroDeckPlugin artifact." };

		var categoryOption = new Option<string[]>("--category")
		{
			Description = "Restrict to one category (repeatable). See --list-checks for the vocabulary.",
			DefaultValueFactory = _ => []
		};
		var checkOption = new Option<string[]>("--check")
			{ Description = "Restrict to one check id (repeatable).", DefaultValueFactory = _ => [] };
		var requiredOnlyOption = new Option<bool>("--required-only") { Description = "Run only Required checks." };
		var reportOption = CreateReportFormatOption();
		var outputOption = new Option<string?>("--output")
			{ Description = "Where to write the report. Defaults to stdout." };
		var timeoutOption = new Option<int?>("--timeout") { Description = "Per-check timeout, in seconds." };
		var listChecksOption = new Option<bool>("--list-checks")
			{ Description = "List every check id, category and requirement level, and exit." };

		var command = new Command("test",
			"Run the plugin conformance suite against a project, executable or artifact.");
		command.Add(projectOption);
		command.Add(executableOption);
		command.Add(artifactOption);
		command.Add(categoryOption);
		command.Add(checkOption);
		command.Add(requiredOnlyOption);
		command.Add(reportOption);
		command.Add(outputOption);
		command.Add(timeoutOption);
		command.Add(listChecksOption);

		command.SetAction(async (parseResult, cancellationToken) =>
		{
			var console = ConsoleFactory.From(parseResult);

			var options = new TestOptions
			{
				Project = parseResult.GetValue(projectOption),
				Executable = parseResult.GetValue(executableOption),
				Artifact = parseResult.GetValue(artifactOption),
				Categories = parseResult.GetValue(categoryOption) ?? [],
				CheckIds = parseResult.GetValue(checkOption) ?? [],
				RequiredOnly = parseResult.GetValue(requiredOnlyOption),
				ReportFormat = parseResult.GetValue(reportOption),
				OutputPath = parseResult.GetValue(outputOption),
				TimeoutSeconds = parseResult.GetValue(timeoutOption),
				ListChecks = parseResult.GetValue(listChecksOption)
			};

			return await TestSession.RunAsync(console, options, cancellationToken).ConfigureAwait(false);
		});

		return command;
	}

	private static Option<ConformanceReportFormat> CreateReportFormatOption()
	{
		var option = new Option<ConformanceReportFormat>("--report")
		{
			Description = "text, json or markdown. Defaults to text.",
			DefaultValueFactory = _ => ConformanceReportFormat.Text
		};

		option.CustomParser = result => CliOptionParsing.ParseToken(result,
			ConformanceReportFormat.Text,
			("text", ConformanceReportFormat.Text),
			("json", ConformanceReportFormat.Json),
			("markdown", ConformanceReportFormat.Markdown));

		return option;
	}
}

/// <summary>Every <c>test</c> option, parsed and handed to <see cref="TestSession" /> as one value.</summary>
internal sealed record TestOptions
{
	public string? Project { get; init; }

	public string? Executable { get; init; }

	public string? Artifact { get; init; }

	public required IReadOnlyList<string> Categories { get; init; }

	public required IReadOnlyList<string> CheckIds { get; init; }

	public bool RequiredOnly { get; init; }

	public required ConformanceReportFormat ReportFormat { get; init; }

	public string? OutputPath { get; init; }

	public int? TimeoutSeconds { get; init; }

	public bool ListChecks { get; init; }
}
