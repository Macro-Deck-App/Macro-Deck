namespace MacroDeck.Sdk.Storage;

/// <summary>What <see cref="IIntegrationStorage.ReadAsync" /> found under a key.</summary>
public enum IntegrationStorageReadStatus
{
	/// <summary>Nothing is stored under the key.</summary>
	Missing,

	/// <summary>A valid copy was found; it is in <see cref="IntegrationStorageReadResult.Content" />.</summary>
	Found,

	/// <summary>Copies exist, but none could be read or none passed the validator.</summary>
	Unreadable
}

/// <summary>The outcome of <see cref="IIntegrationStorage.ReadAsync" />.</summary>
public sealed record IntegrationStorageReadResult
{
	private IntegrationStorageReadResult(IntegrationStorageReadStatus status, string? content)
	{
		Status = status;
		Content = content;
	}

	public static IntegrationStorageReadResult Missing { get; } = new(IntegrationStorageReadStatus.Missing, null);

	public static IntegrationStorageReadResult Unreadable { get; } = new(IntegrationStorageReadStatus.Unreadable, null);

	public IntegrationStorageReadStatus Status { get; }

	/// <summary>The stored text when <see cref="Status" /> is <see cref="IntegrationStorageReadStatus.Found" />, otherwise <c>null</c>.</summary>
	public string? Content { get; }

	public static IntegrationStorageReadResult Found(string content)
	{
		ArgumentNullException.ThrowIfNull(content);
		return new IntegrationStorageReadResult(IntegrationStorageReadStatus.Found, content);
	}
}
