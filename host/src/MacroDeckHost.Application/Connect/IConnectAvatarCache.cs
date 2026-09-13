namespace MacroDeckHost.Application.Connect;

public sealed record ConnectAvatar(Stream Content, string ContentType);

public interface IConnectAvatarCache
{
	event EventHandler? VersionChanged;

	string? Version { get; }

	/// <summary>
	/// Opens the signed-in account's avatar, fetching and caching it on first use. Returns null when the
	/// account has no picture claim or the upstream image is gone - never a placeholder.
	/// </summary>
	Task<ConnectAvatar?> GetAvatar(CancellationToken cancellationToken = default);

	Task Revalidate(CancellationToken cancellationToken = default);
}
