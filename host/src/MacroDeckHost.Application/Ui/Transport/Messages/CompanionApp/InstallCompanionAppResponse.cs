namespace MacroDeckHost.Application.Ui.Transport.Messages.CompanionApp;

public sealed class InstallCompanionAppResponse
{
	public bool Success { get; init; }

	public string? Error { get; init; }

	public required CompanionAppStatus Status { get; init; }
}
