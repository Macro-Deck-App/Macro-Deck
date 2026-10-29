using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MacroDeckHost.Application.Secrets;

namespace MacroDeckHost.Application.Mcp;

public static class McpEntities
{
	public const string Profile = "profile";
	public const string Folder = "folder";
	public const string Widget = "widget";
	public const string Automation = "automation";
	public const string Script = "script";
	public const string Variable = "variable";
}

public enum McpJournalOperation
{
	Created,
	Updated,
	Deleted
}

public sealed class McpJournalItem
{
	public required string Entity { get; init; }

	public required Guid Id { get; init; }

	public required McpJournalOperation Operation { get; init; }

	public JsonElement? Before { get; init; }

	public string? AfterFingerprint { get; set; }

	public Dictionary<Guid, bool>? StartFoldersAfter { get; set; }

	public List<string> ApprovedSnapshots { get; init; } = [];

	public List<JsonElement> CascadedVariables { get; init; } = [];
}

public sealed class McpJournalBatch
{
	public required Guid BatchId { get; init; }

	public required DateTime At { get; init; }

	public required Guid TokenId { get; init; }

	public required string Summary { get; init; }

	public List<McpJournalItem> Items { get; init; } = [];

	public bool Undone { get; set; }
}

public sealed class McpJournalDocument
{
	public List<McpJournalBatch> Batches { get; init; } = [];

	public HashSet<Guid> SecretIds()
	{
		var references = new HashSet<Guid>();
		foreach (var item in Batches.SelectMany(batch => batch.Items))
		{
			if (item.Before is { } before)
			{
				Collect(before, references);
			}

			foreach (var variable in item.CascadedVariables)
			{
				Collect(variable, references);
			}

			foreach (var snapshot in item.ApprovedSnapshots)
			{
				references.UnionWith(SecretReferences.Extract(snapshot));
			}
		}

		return references;
	}

	// Widget data and flows sit in the snapshots as JSON-encoded strings, so every string is searched as text.
	private static void Collect(JsonElement element, HashSet<Guid> references)
	{
		switch (element.ValueKind)
		{
			case JsonValueKind.String:
				references.UnionWith(SecretReferences.Extract(element.GetString()));
				break;
			case JsonValueKind.Object:
				foreach (var property in element.EnumerateObject())
				{
					if (property.Name == "$secret"
						&& property.Value.ValueKind == JsonValueKind.String
						&& Guid.TryParse(property.Value.GetString(), out var id))
					{
						references.Add(id);
					}

					Collect(property.Value, references);
				}

				break;
			case JsonValueKind.Array:
				foreach (var item in element.EnumerateArray())
				{
					Collect(item, references);
				}

				break;
		}
	}
}

public interface IMcpJournal
{
	IReadOnlyList<McpJournalBatch> Batches();

	McpJournalBatch? Find(Guid batchId);

	Task Append(McpJournalBatch batch, CancellationToken cancellationToken = default);

	void MarkUndone(Guid batchId);
}

public sealed class McpJournal : IMcpJournal
{
	public const int Capacity = 20;

	private readonly IMcpStateStore _store;
	private readonly IMcpSecretLedger _secrets;
	private static readonly Lock _gate = new();

	public McpJournal(IMcpStateStore store, IMcpSecretLedger secrets)
	{
		_store = store;
		_secrets = secrets;
	}

	public static string Fingerprint(JsonElement? snapshot)
	{
		var text = snapshot?.GetRawText() ?? string.Empty;
		return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
	}

	public IReadOnlyList<McpJournalBatch> Batches()
	{
		lock (_gate)
		{
			return Document().Batches.ToList();
		}
	}

	public McpJournalBatch? Find(Guid batchId)
	{
		lock (_gate)
		{
			return Document().Batches.FirstOrDefault(batch => batch.BatchId == batchId);
		}
	}

	public async Task Append(McpJournalBatch batch, CancellationToken cancellationToken = default)
	{
		var evicted = false;
		lock (_gate)
		{
			var document = Document();
			document.Batches.Add(batch);
			while (document.Batches.Count > Capacity)
			{
				document.Batches.RemoveAt(0);
				evicted = true;
			}

			_store.Write(IMcpStateStore.JournalDocument, document);
		}

		if (evicted)
		{
			await _secrets.ScrubDeferred(cancellationToken);
		}
	}

	public void MarkUndone(Guid batchId)
	{
		lock (_gate)
		{
			var document = Document();
			var batch = document.Batches.FirstOrDefault(candidate => candidate.BatchId == batchId);
			if (batch is not null)
			{
				batch.Undone = true;
				_store.Write(IMcpStateStore.JournalDocument, document);
			}
		}
	}

	private McpJournalDocument Document()
	{
		return _store.Read<McpJournalDocument>(IMcpStateStore.JournalDocument) ?? new McpJournalDocument();
	}
}
