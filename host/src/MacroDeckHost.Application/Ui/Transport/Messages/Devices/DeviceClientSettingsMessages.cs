namespace MacroDeckHost.Application.Ui.Transport.Messages.Devices;

public sealed record GetDeviceClientSettingsRequest
{
	// Set by the dispatcher from the connection's device claim, never by the client.
	public Guid? DeviceId { get; init; }
}

public sealed record GetDeviceClientSettingsResponse
{
	public bool SettingsButtonHidden { get; init; }
}

public sealed record DeviceClientSettingsChangedEvent
{
	public bool SettingsButtonHidden { get; init; }
}

public sealed record DeviceClientSettingsChange
{
	public bool SettingsButtonHidden { get; init; }
}

public sealed record SetDeviceClientSettingsRequest
{
	// Set by the dispatcher from the connection's device claim, never by the client.
	public required Guid DeviceId { get; init; }

	public bool SettingsButtonHidden { get; init; }
}

public sealed record SetDeviceClientSettingsResponse
{
	public bool Success { get; init; }
}
