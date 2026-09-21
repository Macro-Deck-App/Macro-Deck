using System.Reflection.Metadata;
using MacroDeck.Plugin.Hosting.Capabilities.Ui;

[assembly: MetadataUpdateHandler(typeof(UiPreviewHotReload))]

namespace MacroDeck.Plugin.Hosting.Capabilities.Ui;

// Invoked by the .NET runtime on the hot reload agent's thread: subscribers must leave live sessions to
// their own pumps instead of touching them here.
internal static class UiPreviewHotReload
{
	public static event Action? Updated;

	public static void UpdateApplication(Type[]? updatedTypes) => Updated?.Invoke();
}
