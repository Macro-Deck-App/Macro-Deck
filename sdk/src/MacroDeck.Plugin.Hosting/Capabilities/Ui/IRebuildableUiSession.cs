namespace MacroDeck.Plugin.Hosting.Capabilities.Ui;

internal interface IRebuildableUiSession
{
	IAsyncDisposable Rebuild();
}
