namespace MacroDeckHost.Integrations.Jellyfin.Protocol;

internal interface IJellyfinClient
{
	Task<JellyfinPublicSystemInfo> GetPublicInfoAsync(CancellationToken cancellationToken);

	Task VerifyTokenAsync(CancellationToken cancellationToken);

	Task<JellyfinAuthenticationResult> AuthenticateAsync(
		string username,
		string password,
		CancellationToken cancellationToken);

	Task<IReadOnlyList<JellyfinSessionDto>> GetSessionsAsync(CancellationToken cancellationToken);

	Task SendPlaystateAsync(
		string sessionId,
		string command,
		long? seekPositionTicks,
		CancellationToken cancellationToken);

	Task SendGeneralCommandAsync(
		string sessionId,
		string command,
		IReadOnlyDictionary<string, string>? arguments,
		CancellationToken cancellationToken);

	Task SendMessageAsync(
		string sessionId,
		string? header,
		string text,
		int? timeoutMs,
		CancellationToken cancellationToken);

	Task PlayNowAsync(string sessionId, IReadOnlyList<string> itemIds, CancellationToken cancellationToken);

	Task<IReadOnlyList<JellyfinItemDto>> SearchItemsAsync(
		string searchTerm,
		IReadOnlyList<string> itemTypes,
		string? userId,
		CancellationToken cancellationToken);

	Task<JellyfinImage?> GetPrimaryImageAsync(string itemId, string? tag, CancellationToken cancellationToken);

	Task RunSessionSocketAsync(
		Action<IReadOnlyList<JellyfinSessionDto>> onSessions,
		CancellationToken cancellationToken);
}

internal sealed record JellyfinImage(byte[] Data, string MimeType);

internal sealed record JellyfinServerSettings(Uri BaseUri, string? Token, string DeviceId);

internal sealed class JellyfinAuthenticationException(string message) : Exception(message);

internal sealed class JellyfinRequestException(string message, int? statusCode = null, Exception? inner = null)
	: Exception(message, inner)
{
	public int? StatusCode { get; } = statusCode;
}

internal sealed class JellyfinSocketException(string message, Exception? inner = null) : Exception(message, inner);
