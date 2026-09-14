using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MacroDeckHost.Infrastructure.Licensing;

public sealed record CompanionLicense(
	string Token,
	string LicenseId,
	string Source,
	string KeyId,
	DateTimeOffset IssuedAt)
{
	public bool IsTest => KeyId == CompanionLicenseTokens.TestKeyId;
}

public sealed class CompanionLicenseTokens
{
	public const string Issuer = "https://platform.macro-deck.app";
	public const string Audience = "macrodeck-companion";
	public const string Product = "companion_app_license";
	public const string TestKeyId = "test-2026";

	private const string TestPublicKey =
		"BMdFPTKg5HJXAsVCgNN171OyCGorswlhp7x8ikN/sd5ILXO0aL7NWXsaH5ZQKqeaZzjqXarz5MIg06nglE1Hecw=";

	// Rotation adds a kid here and keeps the old ones, so licenses already issued keep verifying.
	public static readonly IReadOnlyDictionary<string, string> ProductionKeys =
		new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["prod-2026"] = "BKvz2Rtk9QDeRDyADewpWlgUi8O3ni2a6e3JAw/UqJZNcwo1m/0hWynei6Y3BOXtQKn/kGMlqBLb11PoNtDN3+E="
		};

	private static readonly JsonWebTokenHandler Handler = new() { SetDefaultTimesOnTokenCreation = false };
	private static readonly ECDsaSecurityKey TestKey = PublicKey(TestKeyId, TestPublicKey);

	private readonly Dictionary<string, ECDsaSecurityKey> _production;

	public CompanionLicenseTokens(IReadOnlyDictionary<string, string> productionKeys)
	{
		_production = productionKeys.ToDictionary(entry => entry.Key,
			entry => PublicKey(entry.Key, entry.Value),
			StringComparer.Ordinal);
	}

	public async Task<CompanionLicense?> VerifyAsync(string? token, bool trustTestKey)
	{
		if (string.IsNullOrWhiteSpace(token))
		{
			return null;
		}

		var result = await Handler.ValidateTokenAsync(token,
			new TokenValidationParameters
			{
				ValidIssuer = Issuer,
				ValidAudience = Audience,
				ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
				ValidateLifetime = false,
				RequireExpirationTime = false,
				TryAllIssuerSigningKeys = false,
				IssuerSigningKeyResolver = (_, _, kid, _) =>
					Resolve(kid, trustTestKey) is { } key ? [key] : []
			});
		if (!result.IsValid ||
			result.SecurityToken is not JsonWebToken jwt ||
			!jwt.TryGetPayloadValue<string>("product", out var product) ||
			product != Product ||
			!jwt.TryGetPayloadValue<string>("source", out var source) ||
			string.IsNullOrEmpty(source) ||
			string.IsNullOrEmpty(jwt.Subject) ||
			jwt.IssuedAt == DateTime.MinValue)
		{
			return null;
		}

		return new CompanionLicense(token, jwt.Subject, source, jwt.Kid, new DateTimeOffset(jwt.IssuedAt, TimeSpan.Zero));
	}

	public static string Sign(ECDsa privateKey,
		string keyId,
		string licenseId,
		string source,
		DateTimeOffset issuedAt)
		=> Handler.CreateToken(new SecurityTokenDescriptor
		{
			Issuer = Issuer,
			Audience = Audience,
			IssuedAt = issuedAt.UtcDateTime,
			Claims = new Dictionary<string, object>
			{
				[JwtRegisteredClaimNames.Sub] = licenseId,
				["product"] = Product,
				["source"] = source
			},
			// The default factory caches a signer per key id, which outlives a key disposed after signing.
			SigningCredentials = new SigningCredentials(
				new ECDsaSecurityKey(privateKey)
				{
					KeyId = keyId,
					CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
				},
				SecurityAlgorithms.EcdsaSha256)
		});

	private ECDsaSecurityKey? Resolve(string? kid, bool trustTestKey)
	{
		if (kid is null)
		{
			return null;
		}

		if (_production.TryGetValue(kid, out var key))
		{
			return key;
		}

		return trustTestKey && kid == TestKeyId ? TestKey : null;
	}

	private static ECDsaSecurityKey PublicKey(string keyId, string uncompressedPoint)
	{
		var point = Convert.FromBase64String(uncompressedPoint);
		if (point is not [0x04, ..] || point.Length != 65)
		{
			throw new ArgumentException($"Key {keyId} is not an uncompressed P-256 point.", nameof(uncompressedPoint));
		}

		var key = ECDsa.Create(new ECParameters
		{
			Curve = ECCurve.NamedCurves.nistP256,
			Q = new ECPoint { X = point[1..33], Y = point[33..] }
		});
		return new ECDsaSecurityKey(key) { KeyId = keyId };
	}
}
