using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroDeck.Plugin.Testing.Conformance;

/// <summary>Renders a <see cref="ConformanceReport" /> as plain text, JSON or Markdown - three independent views of the same data, never a second source of truth for it.</summary>
public static class ConformanceReportWriter
{
	private static readonly JsonSerializerOptions _jsonOptions = new()
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
	};

	/// <summary>A short, human-readable summary followed by one block per check.</summary>
	public static string ToText(ConformanceReport report)
	{
		ArgumentNullException.ThrowIfNull(report);

		var builder = new StringBuilder();

		builder.Append("Macro Deck plugin conformance report (suite ").Append(report.SuiteVersion).Append(")\n");
		builder.Append("Plugin: ").Append(report.PluginId ?? "(unknown)").Append(' ')
			.Append(report.PluginVersion ?? "(unknown)").Append('\n');
		builder.Append("Started: ")
			.Append(report.StartedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture))
			.Append(", duration: ").Append(report.Duration).Append('\n');
		builder.Append("Passed: ").Append(report.Passed).Append(", Failed: ").Append(report.Failed)
			.Append(", Skipped: ").Append(report.Skipped).Append('\n');
		builder.Append("Conformant: ").Append(report.Conformant ? "yes" : "no").Append("\n\n");

		foreach (var result in report.Results)
		{
			builder.Append('[').Append(Tag(result.Result.Outcome)).Append("] ")
				.Append(result.Id).Append(' ').Append(result.Title)
				.Append(" (").Append(result.Requirement).Append(")\n");

			switch (result.Result.Outcome)
			{
				case ConformanceOutcome.Failed:
					builder.Append("    Expected: ").Append(result.Result.Expected).Append('\n');
					builder.Append("    Actual:   ").Append(result.Result.Actual).Append('\n');
					break;

				case ConformanceOutcome.Skipped:
				case ConformanceOutcome.Inconclusive:
					builder.Append("    Reason:   ").Append(result.Result.SkipReason).Append('\n');
					break;

				case ConformanceOutcome.Passed:
				default:
					break;
			}
		}

		return builder.ToString();
	}

	/// <summary>The report, serialized as indented, camelCase JSON. Every per-check outcome (id and result) round-trips through this text.</summary>
	public static string ToJson(ConformanceReport report)
	{
		ArgumentNullException.ThrowIfNull(report);
		return JsonSerializer.Serialize(report, _jsonOptions);
	}

	/// <summary>A GitHub-flavoured Markdown table, one row per check.</summary>
	public static string ToMarkdown(ConformanceReport report)
	{
		ArgumentNullException.ThrowIfNull(report);

		var builder = new StringBuilder();

		builder.Append("# Macro Deck plugin conformance report\n\n");
		builder.Append("Suite version: `").Append(report.SuiteVersion).Append("`  \n");
		builder.Append("Plugin: `").Append(report.PluginId ?? "(unknown)").Append("` `")
			.Append(report.PluginVersion ?? "(unknown)").Append("`  \n");
		builder.Append("Conformant: **").Append(report.Conformant ? "yes" : "no").Append("**  \n");
		builder.Append("Passed: ").Append(report.Passed).Append(" - Failed: ").Append(report.Failed)
			.Append(" - Skipped: ").Append(report.Skipped).Append("\n\n");

		builder.Append("| Id | Title | Category | Requirement | Outcome | Detail |\n");
		builder.Append("|---|---|---|---|---|---|\n");

		foreach (var result in report.Results)
		{
			var detail = result.Result.Outcome switch
			{
				ConformanceOutcome.Failed =>
					$"Expected: {Escape(result.Result.Expected)}<br>Actual: {Escape(result.Result.Actual)}",
				ConformanceOutcome.Skipped or ConformanceOutcome.Inconclusive => Escape(result.Result.SkipReason),
				_ => string.Empty
			};

			builder.Append("| ").Append(result.Id)
				.Append(" | ").Append(Escape(result.Title))
				.Append(" | ").Append(result.Category)
				.Append(" | ").Append(result.Requirement)
				.Append(" | ").Append(Tag(result.Result.Outcome))
				.Append(" | ").Append(detail)
				.Append(" |\n");
		}

		return builder.ToString();
	}

	private static string Tag(ConformanceOutcome outcome) => outcome switch
	{
		ConformanceOutcome.Passed => "PASS",
		ConformanceOutcome.Failed => "FAIL",
		ConformanceOutcome.Skipped => "SKIP",
		ConformanceOutcome.Inconclusive => "INCONCLUSIVE",
		_ => outcome.ToString()
	};

	private static string Escape(string? text)
		=> (text ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal)
			.Replace("\n", " ", StringComparison.Ordinal);
}
