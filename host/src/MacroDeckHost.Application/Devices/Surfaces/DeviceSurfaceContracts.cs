using MacroDeck.Sdk.Devices;

namespace MacroDeckHost.Application.Devices.Surfaces;

/// <summary>
/// The host-side port a device session renders through. An in-process integration is served by
/// <see cref="InProcess.InProcessDeviceSurfaceProvider" />; a plugin is served by the remote adapter
/// over the plugin transport. Neither the surface service nor anything above it learns which kind it
/// got - that is what keeps the surface pipeline independent of the plugin WebSocket.
/// </summary>
public interface IDeviceSurfaceProvider
{
	/// <summary>Integration id for an in-process provider, plugin id for a remote one.</summary>
	string ProviderId { get; }

	/// <summary>
	/// Offers the session to the provider. A provider that declines - or that has no session support at
	/// all - returns false and the host keeps no session for the device.
	/// </summary>
	Task<bool> OpenAsync(DeviceSurfaceSessionDescriptor session, CancellationToken cancellationToken);

	Task PushAsync(string deviceId, DeviceSurface surface, CancellationToken cancellationToken);

	Task CloseAsync(string deviceId, string? reason, CancellationToken cancellationToken);
}

/// <summary>Identifies the device a session is being opened for, in the ids the provider knows it by.</summary>
public sealed record DeviceSurfaceSessionDescriptor(string DeviceId, string ProviderDeviceId);

/// <summary>Resolves a provider id to whichever adapter serves it.</summary>
public interface IDeviceSurfaceProviderResolver
{
	IDeviceSurfaceProvider? Resolve(string providerId);
}

/// <summary>One deck navigation applied to a single device session.</summary>
public sealed record DeckNavigationCommand
{
	public required string Command { get; init; }

	public string? FolderId { get; init; }

	public string? ProfileId { get; init; }

	public string? NavigationToken { get; init; }

	/// <summary>A resync must not re-fire "Folder Changed" as if the user had navigated.</summary>
	public bool IsResync { get; init; }

	public static DeckNavigationCommand ChangeTo(string folderId, string? profileId, string? navigationToken = null)
		=> new()
		{
			Command = DeckNavigationCommands.ChangeTo,
			FolderId = folderId,
			ProfileId = profileId,
			NavigationToken = navigationToken
		};
}

public static class DeckNavigationCommands
{
	public const string ChangeTo = "changeTo";

	public const string Parent = "parent";

	public const string Back = "back";
}

/// <summary>What the host did with an interaction a device reported.</summary>
public sealed record DeviceInteractionOutcome
{
	public static readonly DeviceInteractionOutcome Accepted = new() { Handled = true };

	/// <summary>True when the host accepted the interaction, whether or not it ran a trigger.</summary>
	public bool Handled { get; init; }

	/// <summary>Set when the interaction was rejected. A stable code, never localized text.</summary>
	public string? ErrorCode { get; init; }

	/// <summary>
	/// True when the kind is part of the contract but has no widget model yet. The interaction is
	/// accepted, nothing is executed, and the session stays open.
	/// </summary>
	public bool Unsupported { get; init; }

	public static DeviceInteractionOutcome Reject(string errorCode) => new() { ErrorCode = errorCode };

	public static readonly DeviceInteractionOutcome NotSupported = new() { Handled = true, Unsupported = true };

	/// <summary>The same verdict in the shape a provider sees it, so an in-process and a remote session
	/// report identically.</summary>
	public DeviceInteractionResult ToResult()
		=> ErrorCode is { } code
			? DeviceInteractionResult.Rejected(code)
			: Unsupported
				? DeviceInteractionResult.NotSupported
				: DeviceInteractionResult.Accepted;
}

/// <summary>The host's own names for the codes a device session reports a refusal under. Deliberately
/// the SDK's values: a provider matches on <see cref="DeviceSessionReasons" /> and the two must not
/// drift apart.</summary>
public static class DeviceSurfaceErrorCodes
{
	public const string SessionNotFound = DeviceSessionReasons.SessionNotFound;

	public const string HostLocked = DeviceSessionReasons.HostLocked;

	public const string WidgetNotOnSurface = DeviceSessionReasons.WidgetNotOnSurface;

	public const string TriggerFailed = DeviceSessionReasons.TriggerFailed;
}

/// <summary>The realtime group key a device session subscribes widget state and label pushes under.</summary>
public static class DeviceSessionGroups
{
	public static string For(Guid deviceId) => $"device-session:{deviceId:D}";
}
