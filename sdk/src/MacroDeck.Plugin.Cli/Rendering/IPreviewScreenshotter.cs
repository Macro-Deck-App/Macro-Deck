namespace MacroDeck.Plugin.Cli.Rendering;

internal interface IPreviewScreenshotter : IAsyncDisposable
{
	Task CaptureAsync(PreviewShot shot, CancellationToken cancellationToken);
}
