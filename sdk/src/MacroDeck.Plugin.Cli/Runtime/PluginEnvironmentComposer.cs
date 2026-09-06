using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Hosting.Configuration;

namespace MacroDeck.Plugin.Cli.Runtime;

/// <summary>
/// Composes a launched plugin's environment exactly as the real supervisor does - see
/// <c>docs/src/content/docs/guides/hosting.md</c>'s "What the supervisor injects" table, which this is
/// a pure, testable restatement of. A pure function deliberately: <c>run</c> is the only caller that
/// actually launches a process, but the composition itself has to be verifiable without spawning one -
/// see the unit tests, which call this directly.
/// <para>
/// The <c>MACRO_DECK_PLUGIN_*</c> variable names are never restated here - every one of them is read
/// out of <see cref="PluginEnvironmentConfiguration.KeysByVariable" />, the same table the SDK's own
/// configuration provider and <c>MacroDeckTestHost.LaunchAsync</c> read, so this can never drift onto a
/// name either of those would not recognise.
/// </para>
/// </summary>
internal static class PluginEnvironmentComposer
{
	/// <summary>
	/// Starts from <paramref name="inherited" /> (defaulting to nothing) with every <c>MACRO_DECK_PLUGIN_*</c>
	/// variable and <c>ASPNETCORE_URLS</c> scrubbed - regardless of whether <paramref name="inherited" />
	/// carried one - then applies exactly <paramref name="request" />'s mode's subset on top, plus
	/// <c>ASPNETCORE_URLS</c> itself. A variable outside that vocabulary (a caller's own
	/// <c>PATH</c>, say) passes through untouched.
	/// </summary>
	public static IReadOnlyDictionary<string, string?> Compose(PluginEnvironmentRequest request,
		IReadOnlyDictionary<string, string?>? inherited = null)
	{
		ArgumentNullException.ThrowIfNull(request);

		var environment = new Dictionary<string, string?>(StringComparer.Ordinal);

		foreach (var (variable, value) in inherited ?? new Dictionary<string, string?>(StringComparer.Ordinal))
		{
			if (!IsScrubbed(variable))
			{
				environment[variable] = value;
			}
		}

		var valuesByConfigKey = ValuesByConfigKey(request);

		foreach (var (variable, configKey) in PluginEnvironmentConfiguration.KeysByVariable)
		{
			if (valuesByConfigKey.TryGetValue(configKey, out var value) && value is not null)
			{
				environment[variable] = value;
			}
		}

		// Not part of PluginEnvironmentConfiguration.KeysByVariable - that table is MACRO_DECK_PLUGIN_*
		// only. ASPNETCORE_URLS is the ordinary ASP.NET Core listener variable, scrubbed above and set
		// here for both modes alike, exactly as the supervisor sets it before the child process starts.
		environment["ASPNETCORE_URLS"] = request.ListenUrl;

		return environment;
	}

	/// <summary>True for a <c>MACRO_DECK_PLUGIN_*</c> variable or <c>ASPNETCORE_URLS</c> - the same two
	/// things <c>plugin-hosting.md</c> says are scrubbed from the child's environment before the
	/// supervisor's own values are applied.</summary>
	public static bool IsScrubbed(string variable)
		=> variable.StartsWith(PluginEnvironmentConfiguration.Prefix, StringComparison.Ordinal) ||
			string.Equals(variable, "ASPNETCORE_URLS", StringComparison.Ordinal);

	private static Dictionary<string, string?> ValuesByConfigKey(PluginEnvironmentRequest request)
	{
		const string Section = PluginHostOptions.SectionName;

		var values = new Dictionary<string, string?>(StringComparer.Ordinal)
		{
			[$"{Section}:Mode"] = request.Mode == PluginRegistrationMode.Managed
				? nameof(PluginRegistrationMode.Managed)
				: nameof(PluginRegistrationMode.SelfRegistering),
			[$"{Section}:HostUrl"] = request.HostUrl,
			[$"{Section}:InstanceId"] = request.InstanceId
		};

		if (request.Mode == PluginRegistrationMode.Managed)
		{
			values[$"{Section}:Id"] = request.PluginId;
			values[$"{Section}:Secret"] = request.Secret;
			values[$"{Section}:DataDirectory"] = request.DataDirectory;
			values[$"{Section}:LaunchId"] = request.LaunchId;
		}
		else
		{
			values[$"{Section}:EnrollmentToken"] = request.EnrollmentToken;
			values[$"{Section}:StateDirectory"] = request.StateDirectory;
			values[$"{Section}:PairingEnabled"] = request.PairingEnabled ? "true" : "false";
		}

		return values;
	}
}
