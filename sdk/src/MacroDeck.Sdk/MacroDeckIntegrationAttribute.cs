namespace MacroDeck.Sdk;

/// <summary>The operating systems an integration can run on, as a set.</summary>
[Flags]
public enum MacroDeckPlatform
{
	Windows = 1,
	MacOS = 2,
	Linux = 4,

	/// <summary>Every platform. What an integration gets unless it says otherwise.</summary>
	All = Windows | MacOS | Linux
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MacroDeckIntegrationAttribute : Attribute
{
	/// <summary>
	/// Where this integration is discovered. <see cref="MacroDeckPlatform.All" /> by default, which is
	/// what almost every integration wants - including the ones with per-platform backends, because a
	/// keystroke or a volume level means something everywhere and only the implementation differs.
	///
	/// <para>
	/// Narrow it only when the thing being integrated <b>does not exist</b> on the other platforms and
	/// nothing can stand in for it - Voicemeeter, for instance, which is a Windows application. Such an
	/// integration is then not instantiated, not registered, and contributes no actions, variables or
	/// events anywhere else; it is as if it had not shipped.
	/// </para>
	/// </summary>
	public MacroDeckPlatform Platforms { get; set; } = MacroDeckPlatform.All;

	/// <summary>
	/// Whether this integration is on until the user turns it off. <c>true</c> by default, which is
	/// what an integration that works out of the box wants - one that needs setup starts disabled
	/// anyway, because it provides a config flow.
	///
	/// <para>
	/// Set it to <c>false</c> for an integration that needs no configuration but still cannot work
	/// unless something is installed alongside it - Voicemeeter, for instance, which would otherwise
	/// report "not available" as an error on every Windows machine that does not have it.
	/// </para>
	/// </summary>
	public bool EnabledByDefault { get; set; } = true;

	/// <summary>Whether this integration should be discovered on the operating system running now.</summary>
	public bool RunsHere() => Platforms.HasFlag(Current);

	/// <summary>The platform the host is running on.</summary>
	public static MacroDeckPlatform Current
	{
		get
		{
			if (OperatingSystem.IsWindows())
			{
				return MacroDeckPlatform.Windows;
			}

			return OperatingSystem.IsMacOS() ? MacroDeckPlatform.MacOS : MacroDeckPlatform.Linux;
		}
	}
}
