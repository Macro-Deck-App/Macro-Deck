namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>
/// Optional behaviours of the host APIs that a plugin opts into with
/// <c>PluginSessionRequest.HostApiFeatures</c>. A plugin that does not name a feature keeps receiving the
/// payloads it was built for.
/// </summary>
public static class HostApiFeatures
{
	/// <summary>The plugin reads <c>color</c> as a script input type in the <see cref="HostApis.Scripts" />
	/// payload. Without it the host sends such an input as <c>text</c>, because an older SDK rejects a
	/// type it does not know and would otherwise lose the whole script list.</summary>
	public const string ScriptInputColor = "scripts.input-color";

	/// <summary>Every feature this protocol version defines.</summary>
	public static readonly IReadOnlyList<string> All = [ScriptInputColor];
}
