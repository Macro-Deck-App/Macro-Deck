namespace MacroDeck.Sdk.Android;

public enum AndroidDeviceAccess
{
	/// <summary>The host has not reported access, or does not offer ADB to plugins.</summary>
	Unsupported = 0,

	Available = 1,

	/// <summary>ADB is switched off in Macro Deck.</summary>
	AdbNotEnabled = 2,

	/// <summary>ADB is on, but this plugin may not use it: plugin access is off, or the manifest does not
	/// declare <c>host:adb</c>.</summary>
	AdbNotAllowed = 3
}
