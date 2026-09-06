using System.Text.Json.Serialization;

namespace MacroDeck.Ui.Model.Resources;

/// <summary>
/// A handle to a resource - an icon, an album cover, any binary asset - referenced by id. This model
/// never carries bytes: no base64, no data URLs. A resource appears as a property value, for example
/// <c>{"source":{"resourceId":"abc123"}}</c>, not as a fixed slot on a node - a node needing several
/// resources is not a closed-slot assumption this model has to make.
///
/// <para>
/// Identity is <see cref="ResourceId" />, never <see cref="ContentHash" />: a hash is a lookup key only
/// if the host computed it over bytes it actually read. A tree is authored by an untrusted plugin, so a
/// hash inside it is declared, not computed here - a cache-validation hint, never a cache path or
/// lookup key.
/// </para>
/// </summary>
public sealed record UiResource
{
	/// <summary>The resource's id. Resolution is a renderer/host concern outside this model.</summary>
	[JsonPropertyOrder(0)]
	public required string ResourceId { get; init; }

	/// <summary>
	/// A declared content hash of the form <c>sha256:&lt;lowercase hex&gt;</c>. This model does
	/// <b>not</b> validate or normalize it - not even casing - because it is producer-declared
	/// provenance, not a value this package can verify against real bytes. Omitted when absent.
	/// </summary>
	[JsonPropertyOrder(1)]
	public string? ContentHash { get; init; }

	/// <summary>The resource's declared media type, for example <c>image/png</c>. Omitted when
	/// absent.</summary>
	[JsonPropertyOrder(2)]
	public string? MediaType { get; init; }

	/// <summary>The resource's declared byte length. Omitted when absent.</summary>
	[JsonPropertyOrder(3)]
	public long? ByteLength { get; init; }
}
