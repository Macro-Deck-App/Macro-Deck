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

	/// <summary>
	/// Refines <see cref="ProtocolErrorCodes.CapabilityUnavailable" /> on an <c>adb</c> call that changes
	/// something: Macro Deck is locked, so it runs nothing until it is unlocked.
	/// </summary>
	public const string HostLocked = "host_locked";

	/// <summary>Refines <see cref="ProtocolErrorCodes.AdbFailed" />: no adb executable was found.</summary>
	public const string AdbExecutableNotFound = "adb_executable_not_found";

	/// <summary>Refines <see cref="ProtocolErrorCodes.AdbFailed" />: the adb server could not be reached.</summary>
	public const string AdbServerUnreachable = "adb_server_unreachable";

	/// <summary>Refines <see cref="ProtocolErrorCodes.AdbFailed" />: no device with that serial is attached.</summary>
	public const string AdbDeviceNotFound = "adb_device_not_found";

	/// <summary>Refines <see cref="ProtocolErrorCodes.AdbFailed" />: the device is attached but offline.</summary>
	public const string AdbDeviceOffline = "adb_device_offline";

	/// <summary>Refines <see cref="ProtocolErrorCodes.AdbFailed" />: the device has not authorized this computer.</summary>
	public const string AdbDeviceUnauthorized = "adb_device_unauthorized";

	/// <summary>Refines <see cref="ProtocolErrorCodes.AdbFailed" />: adb did not finish in time.</summary>
	public const string AdbTimeout = "adb_timeout";

	/// <summary>Refines <see cref="ProtocolErrorCodes.AdbFailed" />: adb ran and reported a failure.</summary>
	public const string AdbCommandFailed = "adb_command_failed";

	/// <summary>Refines <see cref="ProtocolErrorCodes.AdbFailed" />: an argument was rejected before adb ran.</summary>
	public const string AdbInvalidArgument = "adb_invalid_argument";

	/// <summary>Refines <see cref="ProtocolErrorCodes.AdbFailed" />: the device cannot answer this operation.</summary>
	public const string AdbUnsupported = "adb_unsupported";
}
