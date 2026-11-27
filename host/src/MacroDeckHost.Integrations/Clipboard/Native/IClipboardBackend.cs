namespace MacroDeckHost.Integrations.Clipboard.Native;

internal interface IClipboardBackend
{
	ClipboardAvailability Availability { get; }

	Task<ClipboardContentKinds> ReadKindsAsync(CancellationToken cancellationToken);

	Task<ClipboardTextReading> ReadTextAsync(int maxChars, CancellationToken cancellationToken);

	Task<ClipboardWriteReceipt> SetTextAsync(string text, CancellationToken cancellationToken);

	Task<ClipboardWriteReceipt> SetImageAsync(string path, CancellationToken cancellationToken);

	Task<ClipboardWriteReceipt> SetFilesAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken);

	Task<ClipboardWriteReceipt> ClearAsync(CancellationToken cancellationToken);

	IClipboardChangeSource CreateChangeSource();
}
