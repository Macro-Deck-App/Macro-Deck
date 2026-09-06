namespace MacroDeckHost.Application.Connect;

public interface IConnectAvatarCache
{
	/// <summary>
	/// Opens the signed-in account's avatar, fetching and caching it on first use. Returns null when the
	/// account has no picture claim or the upstream image is gone - never a placeholder.
	/// </summary>
	Task<Stream?> GetAvatar(CancellationToken cancellationToken = default);
}
