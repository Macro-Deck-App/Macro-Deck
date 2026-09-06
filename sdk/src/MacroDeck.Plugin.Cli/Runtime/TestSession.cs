using MacroDeck.Plugin.Cli.Commands;
using MacroDeck.Plugin.Testing;
using MacroDeck.Plugin.Testing.Conformance;

namespace MacroDeck.Plugin.Cli.Runtime;

/// <summary>The orchestration <c>test</c>'s command action delegates to.</summary>
internal static class TestSession
{
	public static async Task<int> RunAsync(CliConsole console, TestOptions options, CancellationToken cancellationToken)
	{
		if (options.ListChecks)
		{
			return ListChecks(console, options.OutputPath);
		}

		var selectorCount
			= new[] { options.Project, options.Executable, options.Artifact }.Count(value => value is not null);
		if (selectorCount != 1)
		{
			console.WriteError("invalid-selector-count",
				"Specify exactly one of --project, --executable or --artifact.");
			return ExitCode.UsageError;
		}

		if (!TryParseCategories(console, options.Categories, out var categories))
		{
			return ExitCode.UsageError;
		}

		if (!TryValidateCheckIds(console, options.CheckIds))
		{
			return ExitCode.UsageError;
		}

		var conformanceOptions = new ConformanceOptions
		{
			Categories = categories,
			Ids = options.CheckIds,
			RequiredOnly = options.RequiredOnly,
			PerCheckTimeout = options.TimeoutSeconds is { } seconds
				? TimeSpan.FromSeconds(seconds)
				: new ConformanceOptions().PerCheckTimeout
		};

		ConformanceSubject subject;
		try
		{
			subject = await ResolveSubjectAsync(console, options, cancellationToken).ConfigureAwait(false);
		}
		catch (PluginSubjectException ex)
		{
			console.WriteError(ex.Code, ex.Message, ex.Detail);
			return ExitCode.InputUnreadable;
		}

		try
		{
			ConformanceReport report;

			try
			{
				report = await new ConformanceRunner(conformanceOptions).RunAsync(subject, cancellationToken)
					.ConfigureAwait(false);
			}
			catch (Exception ex) when (ex is PluginProcessExitedException
				or PluginTestTimeoutException
				or InvalidOperationException)
			{
				// ConformanceRunner.RunAsync deliberately lets a subject that never even starts propagate -
				// see its own remarks. That is an input problem (a bad artifact, an executable that will
				// not launch), not a verdict about the plugin's conformance.
				console.WriteError("subject-launch-failed", $"The subject could not be launched: {ex.Message}");
				return ExitCode.InputUnreadable;
			}

			WriteReport(console, options.ReportFormat, options.OutputPath, report);
			return ConformanceReportExitCode.For(report);
		}
		finally
		{
			await subject.DisposeAsync().ConfigureAwait(false);
		}
	}

	private static async Task<ConformanceSubject> ResolveSubjectAsync(CliConsole console,
		TestOptions options,
		CancellationToken cancellationToken)
	{
		if (options.Artifact is { } artifact)
		{
			// ConformanceSubject.Artifact resolves and caches the spec/manifest itself - reused whole,
			// not routed through PluginSubjectResolver.
			return ConformanceSubject.Artifact(artifact);
		}

		var spec = options.Project is { } project
			? await PluginSubjectResolver.ResolveProjectAsync(project, console, cancellationToken).ConfigureAwait(false)
			: PluginSubjectResolver.ResolveExecutable(options.Executable!);

		return ConformanceSubject.Executable(spec);
	}

	private static bool TryParseCategories(CliConsole console,
		IReadOnlyList<string> tokens,
		out List<ConformanceCategory> categories)
	{
		categories = [];

		foreach (var token in tokens)
		{
			if (!ConformanceCategoryTokens.TryParse(token, out var category))
			{
				console.WriteError("unknown-category",
					$"'{token}' is not a known category. Expected one of: " +
					string.Join(", ", ConformanceCategoryTokens.Tokens) +
					".");
				return false;
			}

			categories.Add(category);
		}

		return true;
	}

	private static bool TryValidateCheckIds(CliConsole console, IReadOnlyList<string> ids)
	{
		if (ids.Count == 0)
		{
			return true;
		}

		var known = new HashSet<string>(new ConformanceRunner().Checks.Select(check => check.Id),
			StringComparer.Ordinal);

		foreach (var id in ids)
		{
			if (!known.Contains(id))
			{
				console.WriteError("unknown-check-id",
					$"'{id}' is not a known check id. Run --list-checks to see every id.");
				return false;
			}
		}

		return true;
	}

	private static int ListChecks(CliConsole console, string? outputPath)
	{
		var checks = new ConformanceRunner().Checks;
		var text = string.Join(Environment.NewLine,
			checks.Select(check => $"{check.Id}\t{check.Category}\t{check.Requirement}\t{check.Title}"));

		if (outputPath is not null)
		{
			File.WriteAllText(outputPath, text + Environment.NewLine);
			console.Info($"Wrote {checks.Count} checks to '{outputPath}'.");
		}
		else
		{
			console.WriteLine(text);
		}

		return ExitCode.Success;
	}

	private static void WriteReport(CliConsole console,
		ConformanceReportFormat format,
		string? outputPath,
		ConformanceReport report)
	{
		var text = format switch
		{
			ConformanceReportFormat.Json => ConformanceReportWriter.ToJson(report),
			ConformanceReportFormat.Markdown => ConformanceReportWriter.ToMarkdown(report),
			ConformanceReportFormat.Text => ConformanceReportWriter.ToText(report),
			_ => ConformanceReportWriter.ToText(report)
		};

		if (outputPath is not null)
		{
			File.WriteAllText(outputPath, text);
			console.Info($"Report written to '{outputPath}'.");
		}
		else
		{
			console.WriteLine(text);
		}
	}
}
