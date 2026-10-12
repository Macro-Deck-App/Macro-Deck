namespace MacroDeck.Sdk.Actions;

/// <summary>
/// Ids of option sources Macro Deck answers itself, for an action or event parameter's
/// <see cref="ActionParameter.OptionsSourceId" />. The id is a contract between the integration that declares
/// the parameter and the host that fills it, so it lives here rather than in either.
/// </summary>
/// <remarks>A Macro Deck that does not know an id offers no options for the parameter; the user can still
/// type a value where the parameter allows it.</remarks>
public static class HostOptionsSources
{
	/// <summary>The canonical names of every variable, as options that also allow a typed name.</summary>
	public const string Variables = "macrodeck.variables";

	/// <summary>The canonical names of the user's own variables, as options that also allow a typed
	/// name.</summary>
	public const string UserVariables = "macrodeck.user-variables";

	/// <summary>The devices paired with Macro Deck. Each option value is the device id.</summary>
	public const string Devices = "macrodeck.devices";

	/// <summary>The connected Android devices. Each option value is the device serial; the first option, with
	/// an empty value, stands for the default device.</summary>
	public const string AndroidDevices = "macrodeck.adb-devices";
}
