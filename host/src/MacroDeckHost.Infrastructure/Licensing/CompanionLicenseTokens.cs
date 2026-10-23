using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Serilog;

namespace MacroDeckHost.Infrastructure.Licensing;

public sealed record CompanionLicense(
	string Token,
	string LicenseId,
	string Source,
	string KeyId,
	DateTimeOffset IssuedAt,
	DateTimeOffset? PurchasedAt = null,
	string? BillingId = null)
{
	public bool IsTest => KeyId == CompanionLicenseTokens.TestKeyId;
}

public sealed class CompanionLicenseTokens
{
	public const string Issuer = "https://platform.macro-deck.app";
	public const string Audience = "macrodeck-companion";
	public const string Product = "companion_app_license";
	public const string TestKeyId = "test-2026";
	public const int MaximumBillingIdLength = 256;
	private const long MaximumEpochSeconds = 253402300799;

	private const string TestPublicKey =
		"BMdFPTKg5HJXAsVCgNN171OyCGorswlhp7x8ikN/sd5ILXO0aL7NWXsaH5ZQKqeaZzjqXarz5MIg06nglE1Hecw=";

	// Rotation adds a kid here and keeps the old ones, so licenses already issued keep verifying.
	public static readonly IReadOnlyDictionary<string, string> ProductionKeys =
		new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["prod-2026"] = "BKvz2Rtk9QDeRDyADewpWlgUi8O3ni2a6e3JAw/UqJZNcwo1m/0hWynei6Y3BOXtQKn/kGMlqBLb11PoNtDN3+E="
		};

	public static readonly TimeSpan UnavailableRetryDelay = TimeSpan.FromSeconds(60);

	private static readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

	private readonly Dictionary<string, ECParameters> _productionParameters;
	private readonly ECParameters _testParameters;
	private readonly Func<ECParameters, ECDsa> _createKey;
	private readonly ILogger _logger;
	private readonly TimeProvider _time;
	private readonly Lock _keyLock = new();

	private Keys? _keys;
	private bool _unavailable;
	private DateTimeOffset _retryAt;

	public event EventHandler? VerificationRestored;

	public CompanionLicenseTokens(IReadOnlyDictionary<string, string> productionKeys)
		: this(productionKeys, Serilog.Core.Logger.None, TimeProvider.System)
	{
	}

	public CompanionLicenseTokens(IReadOnlyDictionary<string, string> productionKeys, ILogger logger, TimeProvider time)
		: this(productionKeys, logger, time, ECDsa.Create)
	{
	}

	internal CompanionLicenseTokens(
		IReadOnlyDictionary<string, string> productionKeys,
		ILogger logger,
		TimeProvider time,
		Func<ECParameters, ECDsa> createKey)
	{
		_productionParameters = productionKeys.ToDictionary(entry => entry.Key,
			entry => PublicKeyParameters(entry.Key, entry.Value),
			StringComparer.Ordinal);
		_testParameters = PublicKeyParameters(TestKeyId, TestPublicKey);
		_createKey = createKey;
		_logger = logger;
		_time = time;
	}

	public bool VerificationAvailable
	{
		get
		{
			try
			{
				EnsureKeys();
				return true;
			}
			catch (CompanionLicenseVerificationUnavailableException)
			{
				return false;
			}
		}
	}

	public async Task<CompanionLicense?> VerifyAsync(string? token, bool trustTestKey)
	{
		if (string.IsNullOrWhiteSpace(token))
		{
			return null;
		}

		var keys = EnsureKeys();
		string? resolvedKid = null;
		var result = await _handler.ValidateTokenAsync(token,
			new TokenValidationParameters
			{
				ValidIssuer = Issuer,
				ValidAudience = Audience,
				ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
				ValidateLifetime = false,
				RequireExpirationTime = false,
				TryAllIssuerSigningKeys = false,
				IssuerSigningKeyResolver = (_, _, kid, _) =>
				{
					if (Resolve(keys, kid, trustTestKey) is not { } key)
					{
						return [];
					}

					resolvedKid = kid;
					return [key];
				}
			});
		if (result is { IsValid: false, Exception: SecurityTokenInvalidSignatureException }
			&& resolvedKid is not null)
		{
			ProbeKey(resolvedKid);
		}

		if (!result.IsValid
			|| result.SecurityToken is not JsonWebToken jwt
			|| !jwt.TryGetPayloadValue<string>("product", out var product)
			|| product != Product
			|| !jwt.TryGetPayloadValue<string>("source", out var source)
			|| string.IsNullOrEmpty(source)
			|| string.IsNullOrEmpty(jwt.Subject)
			|| jwt.IssuedAt == DateTime.MinValue)
		{
			return null;
		}

		var (purchasedAt, billingId) = DisplayClaims(jwt);
		return new CompanionLicense(token,
			jwt.Subject,
			source,
			jwt.Kid,
			new DateTimeOffset(jwt.IssuedAt, TimeSpan.Zero),
			purchasedAt,
			billingId);
	}

	private static (DateTimeOffset? PurchasedAt, string? BillingId) DisplayClaims(JsonWebToken jwt)
	{
		try
		{
			using var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(jwt.EncodedPayload));
			var root = payload.RootElement;
			DateTimeOffset? purchasedAt
				= root.TryGetProperty("purchased_at", out var purchased)
				&& purchased.ValueKind == JsonValueKind.Number
				&& purchased.TryGetInt64(out var seconds)
				&& seconds is > 0 and <= MaximumEpochSeconds
					? DateTimeOffset.FromUnixTimeSeconds(seconds)
					: null;
			var billingId = root.TryGetProperty("billing_id", out var billing)
				&& billing.ValueKind == JsonValueKind.String
				&& billing.GetString() is { Length: > 0 and <= MaximumBillingIdLength } value
				&& !value.Any(char.IsControl)
					? value
					: null;
			return (purchasedAt, billingId);
		}
		catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
		{
			return (null, null);
		}
	}

	public static string Sign(
		ECDsa privateKey,
		string keyId,
		string licenseId,
		string source,
		DateTimeOffset issuedAt,
		DateTimeOffset? purchasedAt = null,
		string? billingId = null)
	{
		var claims = new Dictionary<string, object>
		{
			[JwtRegisteredClaimNames.Sub] = licenseId,
			["product"] = Product,
			["source"] = source
		};
		if (purchasedAt is { } purchased)
		{
			claims["purchased_at"] = purchased.ToUnixTimeSeconds();
		}

		if (billingId is not null)
		{
			claims["billing_id"] = billingId;
		}

		return _handler.CreateToken(new SecurityTokenDescriptor
		{
			Issuer = Issuer,
			Audience = Audience,
			IssuedAt = issuedAt.UtcDateTime,
			Claims = claims,
			// The default factory caches a signer per key id, which outlives a key disposed after signing.
			SigningCredentials = new SigningCredentials(new ECDsaSecurityKey(privateKey)
				{
					KeyId = keyId,
					CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
				},
				SecurityAlgorithms.EcdsaSha256)
		});
	}

	private Keys EnsureKeys()
	{
		bool restored;
		Keys keys;
		lock (_keyLock)
		{
			if (_keys is not null)
			{
				return _keys;
			}

			if (_unavailable && _time.GetUtcNow() < _retryAt)
			{
				throw new CompanionLicenseVerificationUnavailableException(null);
			}

			var created = new List<ECDsaSecurityKey>();
			try
			{
				foreach (var (keyId, parameters) in _productionParameters)
				{
					created.Add(SecurityKey(keyId, parameters));
				}

				created.Add(SecurityKey(TestKeyId, _testParameters));
			}
			catch (CryptographicException ex)
			{
				foreach (var partial in created)
				{
					partial.ECDsa.Dispose();
				}

				MarkUnavailable(ex);
				throw new CompanionLicenseVerificationUnavailableException(ex);
			}

			keys = new Keys(created.SkipLast(1).ToDictionary(key => key.KeyId, StringComparer.Ordinal), created[^1]);

			_keys = keys;
			restored = _unavailable;
			_unavailable = false;
		}

		if (restored)
		{
			_logger.Information("Companion license verification is available again");
			VerificationRestored?.Invoke(this, EventArgs.Empty);
		}

		return keys;
	}

	// IdentityModel reports a failing key only as an invalid signature, so a fresh import tells a broken
	// provider apart from a forged token.
	private void ProbeKey(string kid)
	{
		var parameters = kid == TestKeyId ? _testParameters : _productionParameters[kid];
		try
		{
			using var probe = _createKey(parameters);
		}
		catch (CryptographicException ex)
		{
			lock (_keyLock)
			{
				MarkUnavailable(ex);
			}

			throw new CompanionLicenseVerificationUnavailableException(ex);
		}
	}

	private void MarkUnavailable(CryptographicException cause)
	{
		if (!_unavailable)
		{
			_logger.Warning(cause,
				"Companion licenses cannot be verified because the operating system's cryptography provider "
				+ "is unavailable; retrying in {Delay}",
				UnavailableRetryDelay);
		}

		_unavailable = true;
		_retryAt = _time.GetUtcNow() + UnavailableRetryDelay;
		_keys = null;
	}

	private static ECDsaSecurityKey? Resolve(Keys keys, string? kid, bool trustTestKey)
	{
		if (kid is null)
		{
			return null;
		}

		if (keys.Production.TryGetValue(kid, out var key))
		{
			return key;
		}

		return trustTestKey && kid == TestKeyId ? keys.Test : null;
	}

	// The default factory caches a verifier per key id, which would keep using a key replaced after an outage.
	private ECDsaSecurityKey SecurityKey(string keyId, ECParameters parameters)
	{
		return new ECDsaSecurityKey(_createKey(parameters))
		{
			KeyId = keyId, CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
		};
	}

	private static ECParameters PublicKeyParameters(string keyId, string uncompressedPoint)
	{
		var point = Convert.FromBase64String(uncompressedPoint);
		if (point is not [0x04, ..] || point.Length != 65)
		{
			throw new ArgumentException($"Key {keyId} is not an uncompressed P-256 point.", nameof(uncompressedPoint));
		}

		return new ECParameters
		{
			Curve = ECCurve.NamedCurves.nistP256,
			Q = new ECPoint { X = point[1..33], Y = point[33..] }
		};
	}

	private sealed record Keys(IReadOnlyDictionary<string, ECDsaSecurityKey> Production, ECDsaSecurityKey Test);
}
