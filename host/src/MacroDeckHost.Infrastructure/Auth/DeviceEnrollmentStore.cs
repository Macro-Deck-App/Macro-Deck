using System.Collections.Concurrent;
using MacroDeckHost.Application.Auth;

namespace MacroDeckHost.Infrastructure.Auth;

public sealed class DeviceEnrollmentStore : IDeviceEnrollmentStore
{
	private readonly ConcurrentDictionary<string, DateTime> _tokens = new(StringComparer.Ordinal);

	public void Add(string tokenHash, DateTime expiresAt) => _tokens[tokenHash] = expiresAt;

	public bool TryConsume(string tokenHash, out DateTime expiresAt) => _tokens.TryRemove(tokenHash, out expiresAt);

	public void PurgeExpired(DateTime now)
	{
		foreach (var (hash, expiresAt) in _tokens)
		{
			if (expiresAt < now)
			{
				_tokens.TryRemove(hash, out _);
			}
		}
	}
}
