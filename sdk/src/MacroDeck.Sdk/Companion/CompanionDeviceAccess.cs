namespace MacroDeck.Sdk.Companion;

public enum CompanionDeviceAccess
{
	/// <summary>Macro Deck has not reported access, or does not offer Companion devices to this caller.</summary>
	Unsupported = 0,

	Available = 1,

	/// <summary>
	/// This plugin may not use Companion devices: the user has not allowed plugins to use them, or the manifest
	/// does not declare <c>host:companion-devices</c>.
	/// </summary>
	NotAllowed = 2
}
