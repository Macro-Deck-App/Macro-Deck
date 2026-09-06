namespace MacroDeck.Plugin.Protocol.Limits;

/// <summary>
/// Every timeout v1 enforces. <see cref="TimeSpan" /> cannot be <c>const</c>, so these are
/// <c>static readonly</c> - matching <c>AuthDefaults.AccessTokenLifetime</c> in the host.
/// </summary>
public static class ProtocolTimeouts
{
	public static readonly TimeSpan Handshake = TimeSpan.FromSeconds(10);

	public static readonly TimeSpan DefaultRequest = TimeSpan.FromSeconds(30);

	public static readonly TimeSpan CapabilityInvoke = TimeSpan.FromSeconds(30);

	public static readonly TimeSpan AssetUpload = TimeSpan.FromSeconds(60);

	public static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(20);

	public static readonly TimeSpan KeepAliveTimeout = TimeSpan.FromSeconds(60);

	public static readonly TimeSpan SessionResumeWindow = TimeSpan.FromSeconds(60);

	public static readonly TimeSpan GracefulClose = TimeSpan.FromSeconds(5);
}
