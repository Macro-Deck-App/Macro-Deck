using System.Reflection;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Sdk.Deprecation;

namespace MacroDeck.Plugin.Hosting.Compatibility;

/// <summary>
/// Reads the <c>[MacroDeckSdkUsage]</c> attribute the SDK's usage-manifest generator emitted onto the
/// plugin assembly, so the handshake can report which deprecated APIs the plugin actually uses.
///
/// <para>
/// Absence is a normal, meaningful outcome, not an error: a plugin built before this existed, or one
/// that opted the generator out, simply has no attribute. It then reports its SDK version alone, and the
/// host classifies its findings as inferred rather than confirmed - which is the whole point of the
/// distinction (issue #418).
/// </para>
/// </summary>
internal static class SdkUsageManifestReader
{
	/// <summary>
	/// The usage block to send, or null when even the SDK version cannot be determined - in which case
	/// the host reports the plugin's compatibility as unknown rather than inventing a version.
	/// </summary>
	public static PluginSdkUsage? Read(Assembly? pluginAssembly)
	{
		var assembly = pluginAssembly ?? Assembly.GetEntryAssembly();
		var manifest = assembly?.GetCustomAttribute<MacroDeckSdkUsageAttribute>();

		if (manifest is not null)
		{
			return new PluginSdkUsage
			{
				SdkVersion = manifest.SdkVersion,
				DeprecatedApis = manifest.DeprecatedApis,
				Truncated = manifest.Truncated
			};
		}

		// No manifest: fall back to the SDK this plugin is linked against. DeprecatedApis stays null,
		// which is what tells the host "not reported" as opposed to "reported, and none".
		var sdkVersion = typeof(MacroDeckSdkUsageAttribute).Assembly.GetName().Version;

		return sdkVersion is null
			? null
			: new PluginSdkUsage { SdkVersion = sdkVersion.ToString(), DeprecatedApis = null };
	}
}
