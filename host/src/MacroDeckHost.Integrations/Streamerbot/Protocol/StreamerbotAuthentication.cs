using System.Security.Cryptography;
using System.Text;

namespace MacroDeckHost.Integrations.Streamerbot.Protocol;

internal static class StreamerbotAuthentication
{
	public static string CreateResponse(string password, string salt, string challenge)
	{
		var secret = Sha256Base64(password + salt);
		return Sha256Base64(secret + challenge);
	}

	private static string Sha256Base64(string value)
		=> Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
