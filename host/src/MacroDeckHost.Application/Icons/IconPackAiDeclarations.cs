using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Icons;

// An icon pack only ships static assets, so of the shared declaration only generatedAssets applies.
public static class IconPackAiDeclarations
{
	public static PackageAiDeclaration? ToManifest(IconPackAiAssets assets) => assets switch
	{
		IconPackAiAssets.None => new PackageAiDeclaration(),
		IconPackAiAssets.Generated => new PackageAiDeclaration { GeneratedAssets = true },
		_ => null
	};

	public static IconPackAiAssets FromManifest(PackageAiDeclaration? declaration) => declaration switch
	{
		{ GeneratedAssets: true } => IconPackAiAssets.Generated,
		{ Interaction: false, GeneratedContent: false, Services: null or { Count: 0 } } => IconPackAiAssets.None,
		_ => IconPackAiAssets.NotDeclared
	};

	// Merged icons can only weaken a "None": the pack's claim must still hold for every icon in it.
	public static IconPackAiAssets Merge(IconPackAiAssets target, IconPackAiAssets incoming) => (target, incoming) switch
	{
		(_, IconPackAiAssets.Generated) => IconPackAiAssets.Generated,
		(IconPackAiAssets.None, IconPackAiAssets.NotDeclared) => IconPackAiAssets.NotDeclared,
		_ => target
	};
}
