namespace MacroDeck.Sdk.MusicPlayer;

/// <summary>
/// Provider-agnostic repeat mode. <see cref="Context"/> repeats the current
/// album/playlist/queue; <see cref="Track"/> repeats the current track.
/// </summary>
public enum RepeatMode
{
	Off = 0,
	Track = 1,
	Context = 2,
}
