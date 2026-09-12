namespace MacroDeckHost.Integrations.Companion;

public interface ICompanionGateway
{
	event EventHandler<Guid>? StateChanged;

	bool TryGetState(Guid deviceId, out CompanionDeviceState state);

	Task<bool> SendAsync(Guid deviceId, CompanionCommand command, CancellationToken cancellationToken);

	Task<bool> ResumeAutoCreationAsync(CancellationToken cancellationToken);
}

public interface ICompanionGatewayConsumer
{
	void UseGateway(ICompanionGateway gateway);
}

public sealed record CompanionDeviceState(
	int? BatteryLevelPercent,
	bool Charging,
	string? Orientation,
	int? ScreenBrightnessPercent,
	string? Model,
	string? Platform,
	string? AppVersion);

public sealed record CompanionCommand(string Command, int? BrightnessPercent = null, string? Orientation = null)
{
	public const string SetBrightness = "setBrightness";
	public const string SetOrientation = "setOrientation";
	public const string Vibrate = "vibrate";
}
