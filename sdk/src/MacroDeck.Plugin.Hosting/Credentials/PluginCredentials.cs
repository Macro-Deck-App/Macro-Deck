namespace MacroDeck.Plugin.Hosting.Credentials;

/// <summary>
/// What a plugin needs to open a session: who it claims to be, and the secret proving it. The host
/// url is stored alongside because a secret is only meaningful against the host that issued it.
/// </summary>
/// <param name="PluginId">The registered plugin id.</param>
/// <param name="HostUrl">The host that issued the secret.</param>
/// <param name="Secret">The plugin secret, shown once at registration.</param>
public sealed record PluginCredentials(string PluginId, string HostUrl, string Secret);
