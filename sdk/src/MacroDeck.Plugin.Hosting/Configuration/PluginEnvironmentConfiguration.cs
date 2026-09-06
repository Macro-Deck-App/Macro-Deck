using Microsoft.Extensions.Configuration;

namespace MacroDeck.Plugin.Hosting.Configuration;

/// <summary>
/// Maps the <c>MACRO_DECK_PLUGIN_*</c> environment variables onto the <c>MacroDeck:Plugin</c>
/// configuration section.
///
/// <para>
/// A fixed table rather than <c>AddEnvironmentVariables("MACRO_DECK_PLUGIN_")</c>, because that
/// provider strips the prefix and takes the rest verbatim: <c>MACRO_DECK_PLUGIN_HOST_URL</c> would
/// become the key <c>HOST_URL</c>, which binds to nothing. The alternative - naming the variable
/// <c>MACRO_DECK_PLUGIN_HOSTURL</c> - trades a dozen lines here for a variable name nobody would
/// guess.
/// </para>
/// </summary>
public static class PluginEnvironmentConfiguration
{
	/// <summary>Prefix shared by every variable this source reads.</summary>
	public const string Prefix = "MACRO_DECK_PLUGIN_";

	private static readonly Dictionary<string, string> _keysByVariable = new(StringComparer.Ordinal)
	{
		["MACRO_DECK_PLUGIN_MODE"] = "MacroDeck:Plugin:Mode",
		["MACRO_DECK_PLUGIN_HOST_URL"] = "MacroDeck:Plugin:HostUrl",
		["MACRO_DECK_PLUGIN_ID"] = "MacroDeck:Plugin:Id",
		["MACRO_DECK_PLUGIN_SECRET"] = "MacroDeck:Plugin:Secret",
		["MACRO_DECK_PLUGIN_ENROLLMENT_TOKEN"] = "MacroDeck:Plugin:EnrollmentToken",
		["MACRO_DECK_PLUGIN_INSTANCE_ID"] = "MacroDeck:Plugin:InstanceId",
		["MACRO_DECK_PLUGIN_STATE_DIRECTORY"] = "MacroDeck:Plugin:StateDirectory",
		["MACRO_DECK_PLUGIN_DATA_DIRECTORY"] = "MacroDeck:Plugin:DataDirectory",
		["MACRO_DECK_PLUGIN_LAUNCH_ID"] = "MacroDeck:Plugin:LaunchId",
		["MACRO_DECK_PLUGIN_PAIRING"] = "MacroDeck:Plugin:PairingEnabled",
		["MACRO_DECK_PLUGIN_PAIRING_TIMEOUT"] = "MacroDeck:Plugin:PairingTimeout",
		["MACRO_DECK_PLUGIN_HOST_PROCESS_ID"] = "MacroDeck:Plugin:HostProcessId",
		["MACRO_DECK_PLUGIN_HOST_STARTED_AT"] = "MacroDeck:Plugin:HostStartedAt"
	};

	/// <summary>Every variable this source understands, and the configuration key it maps to.</summary>
	public static IReadOnlyDictionary<string, string> KeysByVariable => _keysByVariable;

	/// <summary>Adds the mapped environment variables to the configuration.</summary>
	public static IConfigurationBuilder AddMacroDeckPluginEnvironmentVariables(this IConfigurationBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		return builder.Add(new PluginEnvironmentConfigurationSource());
	}

	internal static Dictionary<string, string?> Read(Func<string, string?> readVariable)
	{
		var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

		foreach (var (variable, key) in _keysByVariable)
		{
			var value = readVariable(variable);
			if (!string.IsNullOrEmpty(value))
			{
				values[key] = value;
			}
		}

		return values;
	}
}

internal sealed class PluginEnvironmentConfigurationSource : IConfigurationSource
{
	public IConfigurationProvider Build(IConfigurationBuilder builder)
		=> new PluginEnvironmentConfigurationProvider();
}

internal sealed class PluginEnvironmentConfigurationProvider : ConfigurationProvider
{
	public override void Load()
		=> Data = PluginEnvironmentConfiguration.Read(Environment.GetEnvironmentVariable);
}
