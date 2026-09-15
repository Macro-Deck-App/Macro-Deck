using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MacroDeckHost.Application.Connect;

namespace MacroDeckHost.Tests.UnitTests.Connect;

internal static class ConnectJwt
{
	public const string IssuerKeyId = "issuer-key-1";

	public static readonly RSA IssuerKey = RSA.Create(2048);

	public static string Create(
		string subject,
		string? name = "Ada Lovelace",
		string? picture = null,
		string? preferredUsername = null,
		string? nonce = null,
		DateTimeOffset? expires = null,
		string issuer = ConnectEndpoints.Issuer,
		string[]? audiences = null,
		string[]? roles = null,
		RSA? signingKey = null,
		string algorithm = "RS256",
		object? rolesClaim = null)
	{
		var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["iss"] = issuer,
			["aud"] = audiences ?? [ConnectEndpoints.ClientId],
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

		if (preferredUsername is not null)
		{
			payload["preferred_username"] = preferredUsername;
		}

		if (nonce is not null)
		{
			payload["nonce"] = nonce;
		}

		if (roles is not null)
		{
			payload["urn:zitadel:iam:org:project:roles"] = roles.ToDictionary(role => role,
				_ => new Dictionary<string, string> { ["390578110567413087"] = "macro-deck.app" });
		}

		if (rolesClaim is not null)
		{
			payload["urn:zitadel:iam:org:project:roles"] = rolesClaim;
		}

		var header = Segment(new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["alg"] = algorithm, ["typ"] = "JWT", ["kid"] = IssuerKeyId
		});
		var signingInput = $"{header}.{Segment(payload)}";

		if (signingKey is null)
		{
			return $"{signingInput}.AAAA";
		}

		byte[] signature;
		lock (signingKey)
		{
			signature = signingKey.SignData(Encoding.ASCII.GetBytes(signingInput),
				HashAlgorithmName.SHA256,
				RSASignaturePadding.Pkcs1);
		}

		return $"{signingInput}.{Base64Url(signature)}";
	}

	public static string Jwks(RSA key)
	{
		var parameters = key.ExportParameters(includePrivateParameters: false);

		return JsonSerializer.Serialize(new
		{
			keys = new[]
			{
				new
				{
					kty = "RSA",
					use = "sig",
					alg = "RS256",
					kid = IssuerKeyId,
					n = Base64Url(parameters.Modulus!),
					e = Base64Url(parameters.Exponent!)
				}
			}
		});
	}

	private static string Segment(Dictionary<string, object?> values)
		=> Base64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values)));

	private static string Base64Url(byte[] bytes)
		=> Convert.ToBase64String(bytes)
			.TrimEnd('=')
			.Replace('+', '-')
			.Replace('/', '_');
}
