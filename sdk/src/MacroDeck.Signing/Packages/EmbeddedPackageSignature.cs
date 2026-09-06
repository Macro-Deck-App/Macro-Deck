using System.Globalization;
using System.Text.Json.Nodes;

namespace MacroDeck.Signing.Packages;

/// <summary>A manifest's embedded <c>signature</c> object, read directly from the <see cref="JsonNode"/>
/// since three of the four signable formats have no typed manifest model in the SDK.</summary>
internal readonly record struct EmbeddedPackageSignature(
	string Algorithm,
	string KeyId,
	string Value,
	DateTimeOffset SignedAt)
{
	/// <summary>True when <paramref name="manifest"/> carries a <c>signature</c> object at all, regardless
	/// of whether its shape is valid - the caller distinguishes "missing" from "malformed".</summary>
	public static bool IsPresent(JsonObject manifest) => manifest["signature"] is not null;

	public static bool TryParse(JsonObject manifest, out EmbeddedPackageSignature signature)
	{
		signature = default;

		if (manifest["signature"] is not JsonObject node)
		{
			return false;
		}

		if (node["algorithm"] is not JsonValue algorithmValue ||
			!algorithmValue.TryGetValue<string>(out var algorithm) ||
			algorithm.Length == 0)
		{
			return false;
		}

		if (node["keyId"] is not JsonValue keyIdValue ||
			!keyIdValue.TryGetValue<string>(out var keyId) ||
			keyId.Length == 0)
		{
			return false;
		}

		if (node["value"] is not JsonValue valueValue ||
			!valueValue.TryGetValue<string>(out var value) ||
			value.Length == 0)
		{
			return false;
		}

		if (node["signedAt"] is not JsonValue signedAtValue ||
			!signedAtValue.TryGetValue<string>(out var signedAtText) ||
			!DateTimeOffset.TryParse(signedAtText,
				CultureInfo.InvariantCulture,
				DateTimeStyles.RoundtripKind,
				out var signedAt))
		{
			return false;
		}

		signature = new EmbeddedPackageSignature(algorithm, keyId, value, signedAt);
		return true;
	}
}
