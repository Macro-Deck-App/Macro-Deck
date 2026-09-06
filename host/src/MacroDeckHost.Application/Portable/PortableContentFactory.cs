namespace MacroDeckHost.Application.Portable;

public static class PortableContentFactory
{
	public static PortableContent FromAssets(PortableArchiveKind kind, PortableAssetBundle assets)
		=> new()
		{
			Kind = kind,
			Icons = [.. assets.Icons],
			Scripts = [.. assets.Scripts],
			Secrets = [.. assets.Secrets],
			Variables = [.. assets.Variables]
		};
}
