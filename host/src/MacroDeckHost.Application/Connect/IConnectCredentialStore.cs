namespace MacroDeckHost.Application.Connect;

public sealed record ConnectCredential(
	string RefreshToken,
	string Subject,
	string? CachedDisplayName,
	string? CachedPictureUrl,
	DateTimeOffset IssuedAtUtc);

public interface IConnectCredentialStore
{
	Task<ConnectCredential?> Load(CancellationToken cancellationToken = default);

	Task Save(ConnectCredential credential, CancellationToken cancellationToken = default);

	Task Clear(CancellationToken cancellationToken = default);
}
