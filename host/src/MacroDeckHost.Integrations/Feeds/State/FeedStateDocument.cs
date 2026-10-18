using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MacroDeck.Sdk.Storage;

namespace MacroDeckHost.Integrations.Feeds.State;

internal static class FeedStatus
{
	public const string Pending = "pending";

	public const string Ok = "ok";

	public const string NotModified = "not-modified";

	public const string Error = "error";
}

// Persisted per feed. The spellings are stored on disk, so they stay as they are.
internal sealed record FeedStateDocument
{
	public const int CurrentVersion = 1;

	public int Version { get; init; } = CurrentVersion;

	public string? Url { get; init; }

	public string? FilterSignature { get; init; }

	public string? ETag { get; init; }

	public string? LastModified { get; init; }

	public string? FeedTitle { get; init; }

	public bool Baselined { get; init; }

	public DateTimeOffset? LastUpdated { get; init; }

	public DateTimeOffset? LastAttempt { get; init; }

	public string Status { get; init; } = FeedStatus.Pending;

	public string? Error { get; init; }

	public int Failures { get; init; }

	public IReadOnlyList<FeedItem> Items { get; init; } = [];

	public IReadOnlyList<string> Seen { get; init; } = [];

	public IReadOnlyList<string> Unread { get; init; } = [];
}

internal static class FeedStateStore
{
	public const int MaxDocumentBytes = 256 * 1024;

	private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	public static string Key(Guid entryId)
	{
		return $"feed-{entryId:N}.json";
	}

	public static async Task<(FeedStateDocument? Document, bool Writable)> LoadAsync(
		IIntegrationStorage storage,
		Guid entryId,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(storage);

		var result = await storage.ReadAsync(Key(entryId), IsValid, cancellationToken).ConfigureAwait(false);
		return result.Status switch
		{
			IntegrationStorageReadStatus.Found => (Deserialize(result.Content!), true),
			IntegrationStorageReadStatus.Unreadable => (null, false),
			_ => (null, true)
		};
	}

	public static Task SaveAsync(
		IIntegrationStorage storage,
		Guid entryId,
		FeedStateDocument document,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(storage);

		return storage.WriteAsync(Key(entryId), Serialize(document), cancellationToken);
	}

	public static Task DeleteAsync(IIntegrationStorage storage, Guid entryId, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(storage);

		return storage.DeleteAsync(Key(entryId), cancellationToken);
	}

	public static string Serialize(FeedStateDocument document)
	{
		ArgumentNullException.ThrowIfNull(document);

		var bounded = document;
		var text = JsonSerializer.Serialize(bounded, _json);
		while (Encoding.UTF8.GetByteCount(text) > MaxDocumentBytes && bounded.Items.Count > 0)
		{
			bounded = bounded with { Items = [.. bounded.Items.Take(bounded.Items.Count - 1)] };
			text = JsonSerializer.Serialize(bounded, _json);
		}

		while (Encoding.UTF8.GetByteCount(text) > MaxDocumentBytes && bounded.Seen.Count > 0)
		{
			bounded = bounded with { Seen = [.. bounded.Seen.Take(bounded.Seen.Count / 2)] };
			text = JsonSerializer.Serialize(bounded, _json);
		}

		return text;
	}

	public static FeedStateDocument? Deserialize(string content)
	{
		try
		{
			var document = JsonSerializer.Deserialize<FeedStateDocument>(content, _json);
			return document is { Version: FeedStateDocument.CurrentVersion } ? document : null;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static bool IsValid(string content)
	{
		return Deserialize(content) is not null;
	}
}
