using System.Text.Json;
using Json.Schema;

namespace MacroDeckHost.Application.Widgets;

internal sealed record WidgetDataProblem
{
	public required string Pointer { get; init; }

	public required string Keyword { get; init; }

	public required string Message { get; init; }
}

internal static class WidgetDataSchema
{
	public static IReadOnlyList<WidgetDataProblem> Validate(JsonSchema schema, JsonElement data)
	{
		var results = schema.Evaluate(data, new EvaluationOptions { OutputFormat = OutputFormat.List });

		var problems = new List<WidgetDataProblem>();
		var seen = new HashSet<(string Pointer, string Keyword)>();

		Collect(results, problems, seen);

		return problems;
	}

	private static void Collect(EvaluationResults node,
		List<WidgetDataProblem> problems,
		HashSet<(string Pointer, string Keyword)> seen)
	{
		if (node.IsValid)
		{
			return;
		}

		if (node.Errors is { Count: > 0 } errors)
		{
			var pointer = node.InstanceLocation.ToString();

			foreach (var (keyword, message) in errors)
			{
				if (seen.Add((pointer, keyword)))
				{
					problems.Add(new WidgetDataProblem
					{
						Pointer = pointer,
						Keyword = keyword,
						Message = message
					});
				}
			}
		}

		foreach (var child in node.Details ?? [])
		{
			Collect(child, problems, seen);
		}
	}
}
