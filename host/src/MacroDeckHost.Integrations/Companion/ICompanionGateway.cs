namespace MacroDeckHost.Integrations.Companion;

public interface ICompanionGateway
{
	event EventHandler<Guid>? StateChanged;

	bool TryGetState(Guid deviceId, out CompanionDeviceState state);

	Task<bool> SendAsync(Guid deviceId, CompanionCommand command, CancellationToken cancellationToken);

	Task<CompanionCommandResult> RequestAsync(Guid deviceId,
		CompanionCommand command,
		CancellationToken cancellationToken);

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
	string? AppVersion)
{
	public IReadOnlySet<string> Capabilities { get; init; } = new HashSet<string>(StringComparer.Ordinal);

	public IReadOnlySet<string> RequestableCapabilities { get; init; } = new HashSet<string>(StringComparer.Ordinal);

	public bool AnswersCommands { get; init; }

	public bool? InFocus { get; init; }

	public string? NetworkType { get; init; }

	public bool? NetworkMetered { get; init; }

	public bool? NetworkValidated { get; init; }

	public string? NetworkName { get; init; }

	public int? CpuUsagePercent { get; init; }

	public int? MemoryUsedPercent { get; init; }
}

public sealed record CompanionCommand(
	string Command,
	int? BrightnessPercent = null,
	string? Orientation = null,
	string? RequestId = null,
	string? ScreenshotMode = null)
{
	public const string SetBrightness = "setBrightness";
	public const string SetOrientation = "setOrientation";
	public const string Vibrate = "vibrate";
	public const string ScreenOn = "screenOn";
	public const string ScreenOff = "screenOff";
	public const string Focus = "focus";
	public const string Screenshot = "screenshot";
	public const string CancelRequest = "cancelRequest";
}

public enum CompanionCommandFailure
{
	NotConnected,
	Removed,
	TimedOut,
	ConsentDenied,
	Unavailable,
	Failed
}

public sealed record CompanionCommandResult(byte[]? Png, CompanionCommandFailure? Failure)
{
	public static CompanionCommandResult Done { get; } = new(null, null);

	public static CompanionCommandResult Unconfirmed { get; } = new(null, null) { IsUnconfirmed = true };

	public bool IsUnconfirmed { get; init; }

	public static CompanionCommandResult Of(byte[] png) => new(png, null);

	public static CompanionCommandResult Failed(CompanionCommandFailure failure) => new(null, failure);
}
