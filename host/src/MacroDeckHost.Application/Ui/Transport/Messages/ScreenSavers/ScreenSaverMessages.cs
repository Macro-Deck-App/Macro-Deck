using MacroDeck.Localization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.ScreenSavers;

public sealed record ScreenSaver
{
	public required string Id { get; init; }

	public required string ProviderId { get; init; }

	public LocalizedText Name { get; init; }

	public LocalizedText Description { get; init; }

	public bool HasConfiguration { get; init; }

	public bool Interactive { get; init; }

	public bool IsBuiltIn { get; init; }
}

public sealed record GetScreenSaversRequest;

public sealed record GetScreenSaversResponse
{
	public IReadOnlyList<ScreenSaver> ScreenSavers { get; init; } = [];
}

public sealed record ScreenSaverCatalogChangedEvent
{
	public IReadOnlyList<ScreenSaver> ScreenSavers { get; init; } = [];
}

public sealed record GetDeviceScreenSaverRequest
{
	// Set by the dispatcher from the connection's device claim, never by the client.
	public Guid? DeviceId { get; init; }
}

public sealed record GetDeviceScreenSaverResponse
{
	public bool Enabled { get; init; }

	public int IdleSeconds { get; init; }
}

public sealed record DeviceScreenSaverChangedEvent
{
	public bool Enabled { get; init; }

	public int IdleSeconds { get; init; }
}

public sealed record ShowDeviceScreenSaverEvent;
