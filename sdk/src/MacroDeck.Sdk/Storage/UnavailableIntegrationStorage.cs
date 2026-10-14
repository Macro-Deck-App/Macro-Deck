namespace MacroDeck.Sdk.Storage;

/// <summary>
/// The <see cref="IIntegrationStorage" /> of a context that has nowhere to keep documents: every read reports
/// <see cref="IntegrationStorageReadStatus.Missing" />, a delete does nothing and a write throws
/// <see cref="InvalidOperationException" />.
/// </summary>
public sealed class UnavailableIntegrationStorage : IIntegrationStorage
{
	public static readonly UnavailableIntegrationStorage Instance = new();

	private UnavailableIntegrationStorage()
	{
	}

	public Task<IntegrationStorageReadResult> ReadAsync(
		string key,
		Func<string, bool>? isValid = null,
		CancellationToken cancellationToken = default)
	{
		IntegrationStorageKeys.Validate(key);
		return Task.FromResult(IntegrationStorageReadResult.Missing);
	}

	public Task WriteAsync(string key, string content, CancellationToken cancellationToken = default)
	{
		IntegrationStorageKeys.Validate(key);
		return Task.FromException(new InvalidOperationException("This integration has no storage."));
	}

	public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
	{
		IntegrationStorageKeys.Validate(key);
		return Task.CompletedTask;
	}
}
