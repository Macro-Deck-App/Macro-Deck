using MacroDeckHost.Application.Common.Security;

namespace MacroDeckHost.Application.Plugins.Pairing;

/// <summary>PKCE (RFC 7636) S256 challenge/verifier handling for interactive pairing.</summary>
public static class PluginPairingCodeChallenge
{
	public static bool IsWellFormed(string? codeChallenge)
	{
		return PkceCodeChallenge.IsWellFormed(codeChallenge);
	}

	public static bool Verify(string codeVerifier, string codeChallenge)
	{
		return PkceCodeChallenge.Verify(codeVerifier, codeChallenge);
	}
}
