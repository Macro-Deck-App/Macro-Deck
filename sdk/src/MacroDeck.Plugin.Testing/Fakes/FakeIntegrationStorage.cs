using System.Collections.Concurrent;
using MacroDeck.Sdk.Storage;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>
/// In-memory <see cref="IIntegrationStorage" /> with the same key rules and read outcomes as Macro Deck's.
/// Seed documents with <see cref="Set" />, and simulate damaged copies with <see cref="SetUnreadable" />.
/// </summary>
public sealed class FakeIntegrationStorage : IIntegrationStorage
{
	private readonly ConcurrentDictionary<string, string?> _documents = new(StringComparer.Ordinal);

	/// <summary>The keys currently holding a document or a damaged copy.</summary>
	public IReadOnlyCollection<string> Keys => [.. _documents.Keys];

	/// <summary>The stored text under <paramref name="key" />, or <c>null</c> when there is none or it is damaged.</summary>
	public string? Get(string key)
	{
		return _documents.GetValueOrDefault(key);
	}

	/// <summary>Stores <paramref name="content" /> under <paramref name="key" /> as if it had been written.</summary>
	public void Set(string key, string content)
	{
		IntegrationStorageKeys.Validate(key);
		_documents[key] = content;
	}

	/// <summary>Makes <paramref name="key" /> hold copies that no read accepts, until it is written or deleted.</summary>
	public void SetUnreadable(string key)
	{
		IntegrationStorageKeys.Validate(key);
		_documents[key] = null;
	}

	/// <inheritdoc />
	public Task<IntegrationStorageReadResult> ReadAsync(
		string key,
		Func<string, bool>? isValid = null,
		CancellationToken cancellationToken = default)
	{
		IntegrationStorageKeys.Validate(key);
		if (!_documents.TryGetValue(key, out var content))
		{
			return Task.FromResult(IntegrationStorageReadResult.Missing);
		}

		return Task.FromResult(content is { Length: > 0 } && (isValid is null || isValid(content))
			? IntegrationStorageReadResult.Found(content)
			: IntegrationStorageReadResult.Unreadable);
	}

	/// <inheritdoc />
	public Task WriteAsync(string key, string content, CancellationToken cancellationToken = default)
	{
		IntegrationStorageKeys.Validate(key);
		ArgumentException.ThrowIfNullOrEmpty(content);
		_documents[key] = content;
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
	{
		IntegrationStorageKeys.Validate(key);
		_documents.TryRemove(key, out _);
		return Task.CompletedTask;
	}
}
