using MacroDeckHost.Application.Store.Model;

namespace MacroDeckHost.Application.Store.Updates;

public static class StoreUpdateScope
{
	// A profile template "update" imports a second copy of the profile, so it is never batched or announced.
	public static bool IsUpdatable(StoreExtensionKind kind) =>
		kind is StoreExtensionKind.Plugin or StoreExtensionKind.IconPack;

	public static string Key(StoreAvailableUpdate update) =>
		$"{update.Kind}:{update.PackageId.ToLowerInvariant()}@{update.LatestVersion}";
}
