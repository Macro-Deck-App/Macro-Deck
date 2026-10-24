using System.Text.Json;
using MacroDeck.Ui.Dsl;

namespace MacroDeck.Ui.Config;

/// <summary>
/// What a <see cref="UiConfigEvents.ContentUploaded" /> event reports: text a code input in content-resource
/// mode uploaded to Macro Deck. The event carries only this description; the provider reads the bytes by
/// <see cref="UploadId" /> through the origin of the event, and only from the session the event came from.
/// </summary>
public sealed record UiContentUpload
{
	/// <summary>The id Macro Deck issued for the upload. Unguessable and valid for a few minutes, until a newer
	/// upload of the same input replaces it, or until the session closes.</summary>
	public required string UploadId { get; init; }

	/// <summary>The uploaded byte count.</summary>
	public required long Length { get; init; }

	/// <summary>The SHA-256 of the uploaded bytes as Macro Deck computed it, in the form
	/// <c>sha256:&lt;lowercase hex&gt;</c> a UI resource's content hash takes. A provider that registers
	/// exactly these bytes as the input's next content resource gets the same hash, which is how the client
	/// knows it can keep its editor state.</summary>
	public required string ContentHash { get; init; }

	/// <summary>Reads a <see cref="UiConfigEvents.ContentUploaded" /> payload. Members the reader does not know
	/// are ignored; a payload without a non-empty <c>uploadId</c> and <c>contentHash</c> and a non-negative
	/// <c>length</c> does not read.</summary>
	public static bool TryRead(UiEventData data, out UiContentUpload? upload)
	{
		upload = null;

		if (data.Raw is not { ValueKind: JsonValueKind.Object } element
			|| !element.TryGetProperty("uploadId", out var id)
			|| id.ValueKind != JsonValueKind.String
			|| id.GetString() is not { Length: > 0 } uploadId
			|| !element.TryGetProperty("length", out var length)
			|| length.ValueKind != JsonValueKind.Number
			|| !length.TryGetInt64(out var byteCount)
			|| byteCount < 0
			|| !element.TryGetProperty("contentHash", out var hash)
			|| hash.ValueKind != JsonValueKind.String
			|| hash.GetString() is not { Length: > 0 } contentHash)
		{
			return false;
		}

		upload = new UiContentUpload { UploadId = uploadId, Length = byteCount, ContentHash = contentHash };

		return true;
	}
}
