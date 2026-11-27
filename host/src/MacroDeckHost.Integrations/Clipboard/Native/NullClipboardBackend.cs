namespace MacroDeckHost.Integrations.Clipboard.Native;

internal sealed class NullClipboardBackend : IClipboardBackend
{
	public NullClipboardBackend(ClipboardAvailability availability)
	{
		Availability = availability;
	}

	public ClipboardAvailability Availability { get; }

	public Task<ClipboardContentKinds> ReadKindsAsync(CancellationToken cancellationToken)
	{
		throw new ClipboardException(ClipboardFailure.Unavailable);
	}

	public Task<ClipboardTextReading> ReadTextAsync(int maxChars, CancellationToken cancellationToken)
	{
		throw new ClipboardException(ClipboardFailure.Unavailable);
	}

	public Task<ClipboardWriteReceipt> SetTextAsync(string text, CancellationToken cancellationToken)
	{
		throw new ClipboardException(ClipboardFailure.Unavailable);
	}

	public Task<ClipboardWriteReceipt> SetImageAsync(string path, CancellationToken cancellationToken)
	{
		throw new ClipboardException(ClipboardFailure.Unavailable);
	}

	public Task<ClipboardWriteReceipt> SetFilesAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
	{
		throw new ClipboardException(ClipboardFailure.Unavailable);
	}

	public Task<ClipboardWriteReceipt> ClearAsync(CancellationToken cancellationToken)
	{
		throw new ClipboardException(ClipboardFailure.Unavailable);
	}

	public IClipboardChangeSource CreateChangeSource()
	{
		return new NullClipboardChangeSource();
	}
}

internal sealed class NullClipboardChangeSource : IClipboardChangeSource
{
	public event Action<long?>? Changed
	{
		add { }
		remove { }
	}

	public event Action? WatchUnsupported
	{
		add { }
		remove { }
	}

	public bool IsWatchUnsupported => false;

	public void Start()
	{
	}

	public void Dispose()
	{
	}
}
