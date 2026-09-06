using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroDeck.Ui.Model.Serialization;

/// <summary>
/// The canonical wire form of every type in this model: <see cref="Options" /> plus one internal map
/// converter, no hand-written recursive writer. A hand-written writer would be a second source of truth
/// for every record's shape, and the first member added without touching it would silently
/// desynchronise from the real one.
///
/// <para>
/// A deliberate copy of the plugin protocol's own <c>PluginProtocolJson.Options</c> shape, not a
/// reference: this package has no reference to that package, and a renderer's code generator has no
/// business with plugin handshake limits.
/// </para>
///
/// <para>
/// Canonical form is a <b>deterministic</b> function of the object graph, not a semantic digest: object
/// member order follows each type's declared <c>[JsonPropertyOrder]</c>, map keys are sorted
/// <see cref="StringComparer.Ordinal" /> ascending, arrays keep their given order, and numbers are
/// written producer-verbatim rather than renormalised through a <c>double</c>. Two graphs that mean the
/// same thing but were built differently - a different number spelling, a different member order inside
/// an opaque <see cref="JsonElement" /> property value - therefore produce different bytes. A tree
/// cannot be content-addressed from this form without a further decision.
/// </para>
/// </summary>
public static class UiCanonicalJson
{
	/// <summary>The maximum JSON nesting depth this model reads and writes. Reading past it throws
	/// <see cref="JsonException" />.</summary>
	public const int MaxDepth = 32;

	/// <summary>
	/// The one <see cref="JsonSerializerOptions" /> instance every type in this model is read and
	/// written with. Read-only (<see cref="JsonSerializerOptions.IsReadOnly" /> is <c>true</c>): one
	/// consumer mutating a shared, mutable static options instance would change every other consumer's
	/// bytes.
	/// </summary>
	public static readonly JsonSerializerOptions Options = BuildOptions();

	/// <summary>Serializes <paramref name="value" /> to a canonical JSON string. Equivalent to
	/// <c>JsonSerializer.Serialize(value, Options)</c>.</summary>
	public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

	/// <summary>Serializes <paramref name="value" /> to canonical UTF-8 JSON bytes, with no byte order
	/// mark.</summary>
	public static byte[] SerializeToUtf8Bytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);

	/// <summary>
	/// Serializes <paramref name="value" /> to a <see cref="JsonElement" /> using the same options, for
	/// composing one canonical value as another's property value. A producer wanting determinism inside
	/// an otherwise-opaque property value uses this rather than building a <see cref="JsonElement" />
	/// some other way.
	/// </summary>
	public static JsonElement ToElement<T>(T value) => JsonSerializer.SerializeToElement(value, Options);

	private static JsonSerializerOptions BuildOptions()
	{
		var options = new JsonSerializerOptions
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			PropertyNameCaseInsensitive = false,
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
			NumberHandling = JsonNumberHandling.Strict,
			AllowTrailingCommas = false,
			ReadCommentHandling = JsonCommentHandling.Disallow,
			MaxDepth = MaxDepth,
			UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
			WriteIndented = false,
		};

		options.Converters.Add(new OrdinalKeyOrderedMapConverter());

		// populateMissingResolver: true - this options instance never set a TypeInfoResolver of its
		// own (there is no source-generated context here), so MakeReadOnly needs the reflection-based
		// default resolver filled in before it can lock the instance.
		options.MakeReadOnly(populateMissingResolver: true);

		return options;
	}
}
