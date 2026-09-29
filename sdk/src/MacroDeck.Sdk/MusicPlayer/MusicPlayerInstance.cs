using MacroDeck.Sdk.Actions;

namespace MacroDeck.Sdk.MusicPlayer;

/// <summary>
/// A selectable music player exposed by a provider - typically one per configured account.
/// <see cref="Id"/> is unique within the provider; <see cref="DisplayName"/> is shown to the user
/// (e.g. "Spotify (alice)").
/// </summary>
public sealed record MusicPlayerInstance(string Id, string DisplayName)
{
	/// <summary>
	/// Options each Music Player widget sets for itself when it shows this instance, e.g. how often an
	/// "Any app" player cycles between apps. The chosen values reach the provider through
	/// <see cref="IMusicPlayerProvider.GetPlayerWithOptions"/>. Empty by default.
	/// </summary>
	/// <remarks>
	/// Only <see cref="ActionParameterType.String"/>, <see cref="ActionParameterType.Number"/>,
	/// <see cref="ActionParameterType.Boolean"/> and <see cref="ActionParameterType.Choice"/> with static
	/// <see cref="ActionParameter.Options"/> are supported. A name must be 1 to 64 letters, digits, hyphens
	/// or underscores, starting with a letter or digit, and unique within the instance. The host skips an
	/// option that breaks these rules and logs a warning. <see cref="ActionParameter.VisibleWhen"/>,
	/// <see cref="ActionParameter.Required"/> and <see cref="ActionParameter.Placeholder"/> are ignored.
	/// </remarks>
	public IReadOnlyList<ActionParameter> Options { get; init; } = [];
}
