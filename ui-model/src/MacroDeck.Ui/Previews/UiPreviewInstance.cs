using MacroDeck.Ui.Runtime;

namespace MacroDeck.Ui.Previews;

/// <summary>One built rendering of a preview scenario, and whatever it owns.</summary>
/// <remarks>
/// Disposing it releases the resources the scenario handed over. The <see cref="View" /> itself owns
/// nothing disposable: its cells die with it once the instance is dropped.
/// </remarks>
public sealed class UiPreviewInstance : IAsyncDisposable
{
	private readonly IAsyncDisposable? _resources;

	internal UiPreviewInstance(UiView view, IAsyncDisposable? resources)
	{
		View = view;
		_resources = resources;
	}

	/// <summary>The view this rendering serves.</summary>
	public UiView View { get; }

	public ValueTask DisposeAsync() => _resources?.DisposeAsync() ?? ValueTask.CompletedTask;
}
