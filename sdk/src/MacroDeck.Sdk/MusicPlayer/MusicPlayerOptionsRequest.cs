namespace MacroDeck.Sdk.MusicPlayer;

/// <summary>
/// Asks a provider for the player one consumer, typically a Music Player widget, shows for an instance
/// with the values it chose for that instance's <see cref="MusicPlayerInstance.Options"/>.
/// </summary>
public sealed record MusicPlayerOptionsRequest
{
	/// <summary>The provider-local instance id, as <see cref="IMusicPlayerProvider.GetInstances"/> returned it.</summary>
	public required string InstanceId { get; init; }

	/// <summary>
	/// One entry per supported declared option, keyed by its name: the chosen value, or the declared default
	/// when none was chosen. Values are <see cref="string"/> for String and Choice options,
	/// <see cref="double"/> for Number options and <see cref="bool"/> for Boolean options.
	/// </summary>
	public required IReadOnlyDictionary<string, object> Options { get; init; }
}
