namespace MacroDeckHost.Application.Plugins.Runtime;

public static class PluginDisplayNameResolver
{
	public static string Resolve(PluginSessionSnapshot? session, string? manifestName, string pluginId)
	{
		if (session is { Origin: PluginSessionOrigin.Managed })
		{
			return session.DisplayName;
		}

		if (session is not null)
		{
			return !string.IsNullOrWhiteSpace(session.DeclaredName) ? session.DeclaredName : session.DisplayName;
		}

		if (!string.IsNullOrWhiteSpace(manifestName))
		{
			return manifestName;
		}

		return pluginId;
	}

	public static string Resolve(PluginSessionIdentity identity, string? declaredName)
	{
		if (identity.Origin == PluginSessionOrigin.Managed)
		{
			return identity.DisplayName;
		}

		return !string.IsNullOrWhiteSpace(declaredName) ? declaredName : identity.DisplayName;
	}
}
