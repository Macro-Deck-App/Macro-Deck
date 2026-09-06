namespace MacroDeckHost.Application.Ui.Transport.Messages.Settings;

public class GetNetworkSettingsResponse
{
	public int PublicPort { get; set; }

	public int DefaultPublicPort { get; set; }

	public int ActivePublicPort { get; set; }

	public bool OverriddenByEnvironment { get; set; }

	public bool ConfiguredPortIgnored { get; set; }

	public bool PublicListenerUnavailable { get; set; }

	public bool RestartRequired { get; set; }

	public bool RestartSupported { get; set; }

	public string? RestartUnsupportedReason { get; set; }

	public int MinimumPublicPort { get; set; }

	public int MaximumPublicPort { get; set; }

	public bool TlsEnabled { get; set; }

	public string TlsMode { get; set; } = string.Empty;

	public int TlsHttpsPort { get; set; }

	public int DefaultTlsHttpsPort { get; set; }

	public bool ActiveTlsEnabled { get; set; }

	public string ActiveTlsMode { get; set; } = string.Empty;

	public int? ActiveTlsHttpsPort { get; set; }

	public string TlsFailure { get; set; } = string.Empty;

	public string TlsRejection { get; set; } = string.Empty;

	public bool TlsCertificateConfigured { get; set; }

	public string? TlsCertificateSource { get; set; }

	public string? TlsCertificateSubject { get; set; }

	public string? TlsCertificateFingerprint { get; set; }

	public string? TlsCertificateNotBefore { get; set; }

	public string? TlsCertificateNotAfter { get; set; }

	public bool TlsCertificateExpired { get; set; }

	public bool TlsCertificateNotYetValid { get; set; }

	public bool TlsCertificateIssuedByAuthority { get; set; }

	public bool TlsAuthorityConfigured { get; set; }

	public string? TlsAuthoritySubject { get; set; }

	public string? TlsAuthorityFingerprint { get; set; }

	public string? TlsAuthorityNotBefore { get; set; }

	public string? TlsAuthorityNotAfter { get; set; }
}
