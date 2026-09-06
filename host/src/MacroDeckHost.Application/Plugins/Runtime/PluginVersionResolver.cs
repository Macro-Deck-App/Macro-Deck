namespace MacroDeckHost.Application.Plugins.Runtime;

public static class PluginVersionResolver
{
	public static string Resolve(string? installedVersion, string? declaredSessionVersion)
		=> installedVersion is { Length: > 0 }
			? installedVersion
			: declaredSessionVersion is { Length: > 0 }
				? declaredSessionVersion
				: PluginRuntimeSnapshot.UnknownVersion;
}
