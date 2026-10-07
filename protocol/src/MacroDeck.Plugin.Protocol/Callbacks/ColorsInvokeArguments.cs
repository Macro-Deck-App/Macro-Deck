namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>Arguments of <c>colors/resolve</c>.</summary>
public sealed record ColorsResolveArguments
{
	/// <summary>A colour, or a Color variable reference.</summary>
	public required string Value { get; init; }

	/// <summary>The widget whose own variables shadow global ones, or absent for global variables only.</summary>
	public string? WidgetId { get; init; }
}

/// <summary>Result of <c>colors/resolve</c>.</summary>
public sealed record ColorsResolveResult
{
	/// <summary>Lowercase <c>#rrggbb</c> or <c>#rrggbbaa</c>, or absent when the value resolves to no colour.</summary>
	public string? Color { get; init; }
}

/// <summary>One watched colour.</summary>
public sealed record ColorWatchDto
{
	/// <summary>The plugin's own id for the watch, unique within its table.</summary>
	public required string WatchId { get; init; }

	public required string Value { get; init; }

	public string? WidgetId { get; init; }
}

/// <summary>Arguments of <c>colors/watches</c>: the plugin's whole table, replacing the previous one.</summary>
public sealed record ColorsWatchesArguments
{
	public IReadOnlyList<ColorWatchDto> Watches { get; init; } = [];
}

/// <summary>One resolved watch in a <c>colors</c> <c>host.state</c> push.</summary>
public sealed record ColorWatchValueDto
{
	public required string WatchId { get; init; }

	/// <summary>The colour, or absent when the watched value resolves to no colour.</summary>
	public string? Color { get; init; }
}

/// <summary>
/// The <c>colors</c> <c>host.state</c> data: every watch of the plugin's current table, resolved. A push
/// with a revision at or below the last one applied in the same session is stale and ignored.
/// </summary>
public sealed record ColorWatchStateDto
{
	public long Revision { get; init; }

	public IReadOnlyList<ColorWatchValueDto> Values { get; init; } = [];
}
