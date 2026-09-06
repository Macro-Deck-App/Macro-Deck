using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroDeck.Signing;

/// <summary>
/// The single <see cref="JsonSerializerOptions"/> for every certificate and signature document this
/// library reads or writes: indented, camelCase, and omitting null properties. A document written with
/// these options always carries a trailing newline, added by <see cref="Serialize{T}"/> - matching how
/// the offline key-generation tool that issues certificates writes them, since a certificate's detached
/// signature covers its exact file bytes.
/// </summary>
public static class SigningJson
{
	/// <summary>The shared serializer configuration for certificate and signature documents.</summary>
	public static readonly JsonSerializerOptions Options = new()
	{
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	/// <summary>Serializes <paramref name="value"/> with <see cref="Options"/> and appends a trailing
	/// newline, matching the exact bytes every signature over a written document is computed over.</summary>
	public static byte[] Serialize<T>(T value)
	{
		var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Options);
		var withNewline = new byte[bytes.Length + 1];
		bytes.CopyTo(withNewline, 0);
		withNewline[^1] = (byte)'\n';
		return withNewline;
	}
}
