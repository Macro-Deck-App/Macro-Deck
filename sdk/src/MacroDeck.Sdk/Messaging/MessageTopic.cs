namespace MacroDeck.Sdk.Messaging;

/// <summary>
/// The grammar of message channel topics and subscription patterns.
/// <para>
/// A topic is two or more dot-separated segments, each of lowercase letters, digits, <c>-</c> and
/// <c>_</c>, starting and ending with a letter or digit, at most <see cref="MaxLength" /> characters in
/// total: <c>obs.scene.changed</c>. A pattern is a topic, or a prefix of one or more segments followed by
/// <c>.*</c>, which matches every topic below the prefix: <c>obs.*</c> matches <c>obs.scene.changed</c>
/// but not <c>obs</c>.
/// </para>
/// </summary>
public static class MessageTopic
{
	public const int MaxLength = 128;

	/// <summary>The largest serialized payload of one message or reply.</summary>
	public const int MaxPayloadBytes = 64 * 1024;

	private const string WildcardSuffix = ".*";

	public static bool IsValidTopic(string? topic)
		=> topic is { Length: > 0 and <= MaxLength } && SegmentsAreValid(topic, minimumSegments: 2);

	public static bool IsValidPattern(string? pattern)
	{
		if (pattern is not { Length: > 0 and <= MaxLength })
		{
			return false;
		}

		return pattern.EndsWith(WildcardSuffix, StringComparison.Ordinal)
			? SegmentsAreValid(pattern[..^WildcardSuffix.Length], minimumSegments: 1)
			: SegmentsAreValid(pattern, minimumSegments: 2);
	}

	/// <summary>Whether <paramref name="topic" /> is delivered to a subscription on <paramref name="pattern" />.</summary>
	public static bool Matches(string pattern, string topic)
	{
		ArgumentNullException.ThrowIfNull(pattern);
		ArgumentNullException.ThrowIfNull(topic);

		if (!pattern.EndsWith(WildcardSuffix, StringComparison.Ordinal))
		{
			return string.Equals(pattern, topic, StringComparison.Ordinal);
		}

		var prefix = pattern[..^1];
		return topic.Length > prefix.Length && topic.StartsWith(prefix, StringComparison.Ordinal);
	}

	private static bool SegmentsAreValid(string value, int minimumSegments)
	{
		var segments = value.Split('.');
		return segments.Length >= minimumSegments && segments.All(IsValidSegment);
	}

	private static bool IsValidSegment(string segment)
		=> segment.Length > 0 &&
			IsLetterOrDigit(segment[0]) &&
			IsLetterOrDigit(segment[^1]) &&
			segment.All(character => IsLetterOrDigit(character) || character is '-' or '_');

	private static bool IsLetterOrDigit(char character) => character is >= 'a' and <= 'z' or >= '0' and <= '9';
}
