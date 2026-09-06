using MacroDeck.Plugin.Protocol.Logging;

namespace MacroDeck.Plugin.Testing;

/// <summary>
/// One structured log event collected from a plugin under test, whether observed on the wire as a
/// <c>log.publish</c> batch (<see cref="MacroDeckTestHost" />) or captured directly from the plugin's
/// own logging pipeline (<see cref="PluginTestHarness" />). Both paths normalise to this one shape, so
/// an assertion written against it holds regardless of which host ran the plugin.
/// </summary>
public sealed record CollectedLogEvent
{
	/// <summary>When the event was logged, in the plugin's own clock.</summary>
	public required DateTimeOffset Timestamp { get; init; }

	/// <summary>One of <see cref="LogLevels" />.</summary>
	public required string Level { get; init; }

	/// <summary>The logger category, e.g. the fully-qualified type name behind an <c>ILogger&lt;T&gt;</c>.</summary>
	public string? SourceContext { get; init; }

	/// <summary>The unrendered template, e.g. <c>"Initialized with location {Location}."</c>.</summary>
	public required string MessageTemplate { get; init; }

	/// <summary>The rendered text of <see cref="MessageTemplate" /> with its arguments substituted.</summary>
	public required string Message { get; init; }

	/// <summary>Structured properties captured from the log call, pre-rendered to strings.</summary>
	public IReadOnlyDictionary<string, string> Properties { get; init; }
		= new Dictionary<string, string>(StringComparer.Ordinal);

	/// <summary>The exception attached to the event, when there was one.</summary>
	public LogExceptionDto? Exception { get; init; }
}
