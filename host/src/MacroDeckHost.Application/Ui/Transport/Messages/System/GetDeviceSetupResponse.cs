namespace MacroDeckHost.Application.Ui.Transport.Messages.System;

public class DeviceSetupCertificateAuthorityDto
{
	public bool Available { get; set; }

	public string DownloadPath { get; set; } = string.Empty;

	public string? Subject { get; set; }

	public string? FingerprintSha256 { get; set; }

	public string? NotAfter { get; set; }
}

public class GetDeviceSetupResponse
{
	public string InstanceName { get; set; } = string.Empty;

	public bool HttpsEnabled { get; set; }

	public int? HttpsPort { get; set; }

	public DeviceSetupCertificateAuthorityDto CertificateAuthority { get; set; } = new();

	public List<string> HostCertificateSubjectAlternativeNames { get; set; } = [];
}

public class DeviceSetupDiagnosticsReport
{
	public string? Stage { get; set; }

	public string? Message { get; set; }

	public string? Url { get; set; }

	public string? UserAgent { get; set; }
}
