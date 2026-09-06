using MacroDeckHost.Application.Ui.Resources;

namespace MacroDeckHost.Widgets.DeveloperPreviews;

/// <summary>
/// The resource store first-party preview scenarios register their icons in.
/// </summary>
/// <remarks>
/// A preview scenario is static and takes no arguments - that is what lets it be declared beside the view
/// it previews - so it cannot be handed the host's <see cref="IUiResourceStore" /> by injection. A
/// resource registered anywhere else is never served, and its images render as empty boxes, so the store
/// the host really serves is published here instead. <see cref="WidgetUiPreviewSource" /> sets it from
/// the composition root; until then it is a private store, which is what keeps a scan outside the host -
/// a unit test - working without one.
/// </remarks>
internal static class WidgetPreviewResources
{
	private static IUiResourceStore _store = new UiResourceStore();

	public static IUiResourceStore Store => _store;

	public static void Use(IUiResourceStore store) => _store = store;
}
