namespace MacroDeckHost.Application.Ui.Transport.Messages.Settings;

public class UpdateNetworkTlsCertificateRequest
{
	public string CertificatePem { get; set; } = string.Empty;

	public string PrivateKeyPem { get; set; } = string.Empty;
}
