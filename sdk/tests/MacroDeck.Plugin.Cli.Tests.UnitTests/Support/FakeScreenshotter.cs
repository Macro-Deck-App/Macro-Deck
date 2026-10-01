using MacroDeck.Plugin.Cli.Rendering;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests.Support;

internal sealed class FakeScreenshotter : IPreviewScreenshotter
{
	public List<PreviewShot> Shots { get; } = [];

	public Func<PreviewShot, PreviewRenderException?>? Fail { get; init; }

	public Task<IPreviewScreenshotter> Factory(string browser, CancellationToken cancellationToken)
		=> Task.FromResult<IPreviewScreenshotter>(this);

	public Task CaptureAsync(PreviewShot shot, CancellationToken cancellationToken)
	{
		Shots.Add(shot);

		if (Fail?.Invoke(shot) is { } failure)
		{
			throw failure;
		}

		File.WriteAllBytes(shot.OutputPath, [0x89]);

		return Task.CompletedTask;
	}

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
