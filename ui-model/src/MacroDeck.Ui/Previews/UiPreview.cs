using MacroDeck.Ui.Dsl;

namespace MacroDeck.Ui.Previews;

/// <summary>
/// What a preview scenario returns when it owns something that has to be released.
/// </summary>
/// <remarks>
/// A scenario that only builds an element from plain mock data can return the
/// <see cref="Dsl.UiElement" /> directly and never mention this type. It exists for the scenario whose
/// mock starts something - a timer, a subscription, a fake connection: handing those over here is what
/// lets Macro Deck stop them when the preview is switched, refreshed or closed, which nothing else can
/// do on the scenario's behalf.
/// </remarks>
public sealed class UiPreview
{
	private UiPreview(UiElement root, IAsyncDisposable? resources)
	{
		Root = root;
		Resources = resources;
	}

	/// <summary>The authored tree this scenario shows.</summary>
	public UiElement Root { get; }

	/// <summary>What to release when the preview ends, or <c>null</c> when the scenario owns
	/// nothing.</summary>
	public IAsyncDisposable? Resources { get; }

	/// <summary>A scenario that owns nothing beyond the tree itself.</summary>
	public static UiPreview Of(UiElement root)
	{
		ArgumentNullException.ThrowIfNull(root);

		return new UiPreview(root, resources: null);
	}

	/// <summary>A scenario whose mocks must be disposed when the preview ends.</summary>
	public static UiPreview Of(UiElement root, IAsyncDisposable resources)
	{
		ArgumentNullException.ThrowIfNull(root);
		ArgumentNullException.ThrowIfNull(resources);

		return new UiPreview(root, resources);
	}

	/// <summary>A scenario whose mocks must be disposed when the preview ends.</summary>
	public static UiPreview Of(UiElement root, IDisposable resources)
	{
		ArgumentNullException.ThrowIfNull(root);
		ArgumentNullException.ThrowIfNull(resources);

		return new UiPreview(root, new SyncResources(resources));
	}

	private sealed class SyncResources : IAsyncDisposable
	{
		private readonly IDisposable _inner;

		public SyncResources(IDisposable inner) => _inner = inner;

		public ValueTask DisposeAsync()
		{
			_inner.Dispose();

			return ValueTask.CompletedTask;
		}
	}
}
