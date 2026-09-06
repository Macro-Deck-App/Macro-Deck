using System.Text;
using System.Text.Json;
using MacroDeckHost.Application.Connect;

namespace MacroDeckHost.Tests.UnitTests.Connect;

internal static class ConnectJwt
{
	public static string Create(
		string subject,
		string? name = "Ada Lovelace",
		string? picture = null,
		string? creatorUsername = null,
		IReadOnlyList<string>? roles = null,
		string? nonce = null,
		DateTimeOffset? expires = null,
		string issuer = ConnectEndpoints.Issuer,
		string audience = ConnectEndpoints.ClientId)
	{
		var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["iss"] = issuer,
			["aud"] = audience,
			["sub"] = subject,
			["exp"] = (expires ?? DateTimeOffset.UtcNow.AddYears(10)).ToUnixTimeSeconds()
		};

		if (name is not null)
		{
			payload["name"] = name;
		}

		if (picture is not null)
		{
			payload["picture"] = picture;
		}

		if (creatorUsername is not null)
		{
			payload["macrodeck:creator-username"] = creatorUsername;
		}

		if (roles is { Count: 1 })
		{
			payload["role"] = roles[0];
		}
		else if (roles is { Count: > 1 })
		{
			payload["role"] = roles;
		}

		if (nonce is not null)
		{
			payload["nonce"] = nonce;
		}

		var header = Segment(new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["alg"] = "RS256", ["typ"] = "JWT"
		});

		// The reader deliberately does not validate the signature, so any well-formed segment will do.
		return $"{header}.{Segment(payload)}.AAAA";
	}

	private static string Segment(Dictionary<string, object?> values)
		=> Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values)))
			.TrimEnd('=')
			.Replace('+', '-')
			.Replace('/', '_');
}
