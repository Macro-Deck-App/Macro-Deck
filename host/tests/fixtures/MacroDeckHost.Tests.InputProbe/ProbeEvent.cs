using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroDeckHost.Tests.InputProbe;

public sealed record ProbeEvent
{
	public static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	public required string Type { get; init; }

	public bool? Down { get; init; }

	public int? Code { get; init; }

	public int? Scan { get; init; }

	public bool? Extended { get; init; }

	public long? Keysym { get; init; }

	public string? Modifiers { get; init; }

	public string? Text { get; init; }

	public string? Button { get; init; }

	public int? Clicks { get; init; }

	public int? X { get; init; }

	public int? Y { get; init; }

	public int? Width { get; init; }

	public int? Height { get; init; }

	public string? Axis { get; init; }

	public int? Delta { get; init; }

	public int? Pid { get; init; }

	public long? Window { get; init; }

	public bool? Synthetic { get; init; }

	public string? Layout { get; init; }

	public string? Level { get; init; }

	public string? Message { get; init; }

	public bool HasModifier(string modifier)
	{
		return Modifiers?.Split(',').Contains(modifier, StringComparer.Ordinal) == true;
	}
}
