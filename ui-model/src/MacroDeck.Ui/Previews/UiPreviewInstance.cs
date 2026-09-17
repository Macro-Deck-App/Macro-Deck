using MacroDeck.Ui.Runtime;

namespace MacroDeck.Ui.Previews;

/// <summary>One built rendering of a preview scenario, and whatever it owns.</summary>
/// <remarks>
/// Disposing it disposes the <see cref="View" />, including a view the scenario returned itself, and then
/// releases the resources the scenario handed over.
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

	public ValueTask DisposeAsync()
	{
		View.Dispose();

		return _resources?.DisposeAsync() ?? ValueTask.CompletedTask;
	}
}
