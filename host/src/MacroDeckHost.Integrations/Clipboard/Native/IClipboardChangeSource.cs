namespace MacroDeckHost.Integrations.Clipboard.Native;

internal interface IClipboardChangeSource : IDisposable
{
	event Action<long?>? Changed;

	event Action? WatchUnsupported;

	bool IsWatchUnsupported { get; }

	void Start();
}
