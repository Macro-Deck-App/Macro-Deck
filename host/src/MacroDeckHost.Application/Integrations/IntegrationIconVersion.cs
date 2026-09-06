using System.Security.Cryptography;
using MacroDeck.Sdk;

namespace MacroDeckHost.Application.Integrations;

/// <summary>
/// Identifies the current bytes of an integration icon. The icon endpoint is keyed by the
/// integration id alone, which does not change when a plugin ships a new icon - so both the HTTP
/// validator and the URL the UI builds derive their identity from the bytes here, never from the id.
/// </summary>
public static class IntegrationIconVersion
{
	private const int TokenLength = 16;

	public static string? For(IIntegration integration)
		=> integration is IIntegrationIconProvider iconProvider ? Of(iconProvider.GetIcon()) : null;

	public static string? Of(byte[]? iconBytes)
		=> iconBytes is null ? null : Convert.ToHexStringLower(SHA256.HashData(iconBytes))[..TokenLength];
}
