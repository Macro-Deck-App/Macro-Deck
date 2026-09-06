using System.Security.Cryptography.X509Certificates;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Network.Tls;
using MacroDeckHost.Application.Ui.Transport.Messages.System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;


namespace MacroDeckHost.Api.Controllers;

/// <summary>
/// Everything a device needs before it can trust this host, served anonymously on purpose: a browser
/// that does not trust the HTTPS certificate yet cannot sign in, so gating this behind authentication
/// would make the setup it describes impossible to complete. Only public certificate material is ever
/// returned.
/// </summary>
[ApiController]
[Route("api/device-setup")]
public class DeviceSetupController : ControllerBase
{
	public const string CertificateAuthorityDownloadPath = "/api/device-setup/certificate-authority.crt";

	private const string CertificateAuthorityContentType = "application/x-x509-ca-cert";

	private const int MaxReportedFieldLength = 2000;

	private readonly IPublicTlsCertificateStore _certificateStore;
	private readonly IHostListenerState _listenerState;
	private readonly Serilog.ILogger _logger;

	public DeviceSetupController(IPublicTlsCertificateStore certificateStore,
		IHostListenerState listenerState,
		Serilog.ILogger logger)
	{
		_certificateStore = certificateStore;
		_listenerState = listenerState;
		_logger = logger;
	}

	[HttpGet]
	[AllowAnonymous]
	public GetDeviceSetupResponse GetDeviceSetup()
	{
		Response.Headers.CacheControl = "no-store";

		var authority = _certificateStore.ReadAuthorityInfo();

		return new GetDeviceSetupResponse
		{
			InstanceName = Environment.MachineName,
			HttpsEnabled = _listenerState.PublicEndpoints.HttpsPort is not null,
			HttpsPort = _listenerState.PublicEndpoints.HttpsPort,
			CertificateAuthority = new DeviceSetupCertificateAuthorityDto
			{
				Available = authority is not null,
				DownloadPath = CertificateAuthorityDownloadPath,
				Subject = authority?.Subject,
				FingerprintSha256 = authority?.Fingerprint,
				NotAfter = authority?.NotAfter.ToString("O")
			},
			HostCertificateSubjectAlternativeNames = ReadHostCertificateNames()
		};
	}

	[HttpGet("certificate-authority.crt")]
	[AllowAnonymous]
	public IActionResult GetCertificateAuthority()
	{
		if (_certificateStore.ReadAuthorityCertificatePem() is not { } authorityPem)
		{
			Response.StatusCode = StatusCodes.Status404NotFound;
			Response.ContentLength = 0;
			return new EmptyResult();
		}

		using var authority = X509Certificate2.CreateFromPem(authorityPem);

		Response.Headers.CacheControl = "no-store";

		// DER rather than PEM, and deliberately no Content-Disposition: iOS only offers to install a
		// configuration profile for a navigation that lands on this content type, and an attachment
		// disposition suppresses that sheet. Android's importer accepts the same bytes.
		return File(authority.RawData, CertificateAuthorityContentType);
	}

	/// <summary>
	/// Carries a client-side bootstrap failure off a device that cannot report it any other way. The
	/// browsers this exists for predate the developer tooling needed to read their own console, and a
	/// client that fails to start cannot authenticate, so this has to be anonymous to be reachable at
	/// all. Opt-in from the client side, logged and discarded - nothing is stored or echoed back, and
	/// every field is truncated because the payload is attacker-controlled.
	/// </summary>
	[HttpPost("diagnostics")]
	[AllowAnonymous]
	public IActionResult ReportDiagnostics([FromBody] DeviceSetupDiagnosticsReport report)
	{
		_logger.Warning("Web client diagnostics [{Stage}] from {UserAgent} at {Url}: {Message}",
			Truncate(report.Stage),
			Truncate(report.UserAgent),
			Truncate(report.Url),
			Truncate(report.Message));

		return NoContent();
	}

	private static string Truncate(string? value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return string.Empty;
		}

		return value.Length <= MaxReportedFieldLength ? value : value[..MaxReportedFieldLength];
	}

	private List<string> ReadHostCertificateNames()
	{
		if (_certificateStore.ReadCertificatePem() is not { } certificatePem)
		{
			return [];
		}

		try
		{
			using var certificate = X509Certificate2.CreateFromPem(certificatePem);
			return [.. LocalCertificateAuthority.ReadSubjectAlternativeNames(certificate)];
		}
		catch (Exception)
		{
			return [];
		}
	}
}
