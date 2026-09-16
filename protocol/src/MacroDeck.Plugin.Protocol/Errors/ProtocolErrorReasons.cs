namespace MacroDeck.Plugin.Protocol.Errors;

/// <summary>
/// Values for the <c>reason</c> entry a <see cref="ProtocolError" /> may carry in its details. A
/// reason refines an error code that is deliberately kept generic for older clients; a client that
/// does not recognise one must fall back to handling the code alone.
/// </summary>
public static class ProtocolErrorReasons
{
	/// <summary>
	/// The host refused because Developer Mode is switched off. Applies to enrolment, interactive
	/// pairing, and session creation for a plugin holding a development credential.
	/// </summary>
	public const string DeveloperModeDisabled = "developer_mode_disabled";

	/// <summary>
	/// Refines <see cref="ProtocolErrorCodes.PluginAlreadyRegistered" /> on enrolment with a Developer
	/// token: the id belongs to a plugin Macro Deck has installed. Interactive pairing with Developer Mode
	/// on can take that plugin over temporarily instead; retrying the enrolment never helps.
	/// </summary>
	public const string PluginInstalled = "plugin_installed";
}
