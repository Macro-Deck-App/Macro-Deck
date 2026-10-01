using System.Text.Json;

namespace MacroDeck.Plugin.Testing;

/// <summary>What opening one developer preview through <see cref="UiTestClient.OpenPreviewAsync" /> produced.</summary>
public sealed record UiPreviewOutcome
{
	/// <summary>The preview that was asked for.</summary>
	public required string PreviewId { get; init; }

	/// <summary>The id of the session the plugin accepted, or <c>null</c> when it declined the open.</summary>
	public string? SessionId { get; init; }

	/// <summary>Whether the plugin accepted the session and served a tree.</summary>
	public required bool Accepted { get; init; }

	/// <summary>Why the plugin declined the open, or the fault code it reported while building the tree.</summary>
	public string? FailureReason { get; init; }

	/// <summary>The first full tree the plugin served, as the wire carried it. Later patches are not applied.</summary>
	public JsonElement? Tree { get; init; }
}
