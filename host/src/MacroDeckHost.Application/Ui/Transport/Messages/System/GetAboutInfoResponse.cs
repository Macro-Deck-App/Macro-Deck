namespace MacroDeckHost.Application.Ui.Transport.Messages.System;

public class GetAboutInfoResponse
{
	public string Version { get; set; } = string.Empty;

	public bool IsBeta { get; set; }

	public bool IsDevelopmentBuild { get; set; }

	public string? Commit { get; set; }

	public string? BuildTimestamp { get; set; }

	public string? BuildNumber { get; set; }

	public string License { get; set; } = string.Empty;

	public string RuntimeVersion { get; set; } = string.Empty;

	public string OperatingSystem { get; set; } = string.Empty;
}
