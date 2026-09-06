using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Serilog;

namespace MacroDeckHost.Application.Network.Tls;

public readonly record struct PublicTlsBootstrapResult(PublicTlsBootstrapAction Action, bool CertificateChanged);

public sealed class PublicTlsBootstrapper
{
	private readonly IPublicTlsCertificateStore _store;
	private readonly ILocalAddressProvider _addressProvider;
	private readonly IHostNameProvider _hostNameProvider;
	private readonly ILogger _logger;
	private readonly IPublicTlsCertificateHolder? _certificateHolder;

	public PublicTlsBootstrapper(IPublicTlsCertificateStore store,
		ILocalAddressProvider addressProvider,
		IHostNameProvider hostNameProvider,
		ILogger logger,
		IPublicTlsCertificateHolder? certificateHolder = null)
	{
		_store = store;
		_addressProvider = addressProvider;
		_hostNameProvider = hostNameProvider;
		_logger = logger;
		_certificateHolder = certificateHolder;
	}

	public PublicTlsBootstrapResult Run(bool keyRingLocked, DateTimeOffset now)
	{
		try
		{
			var state = BuildState(keyRingLocked);
			var action = PublicTlsBootstrapPlanner.Decide(state, now);

			if (action == PublicTlsBootstrapAction.None &&
				state is { Authority: not null, AuthorityKeyUsable: false, KeyRingLocked: false })
			{
				_logger.Warning("The local certificate authority exists but its private key could not be " +
					"opened, so the host certificate was left untouched. Devices that already trust this " +
					"installation keep working; restore a backup or regenerate the authority to issue new " +
					"certificates.");
			}

			return Execute(action, now);
		}
		catch (Exception ex) when (ex is CryptographicException
			or IOException
			or UnauthorizedAccessException
			or ArgumentException)
		{
			// A certificate problem must never be fatal: the loopback settings screen is the only place
			// the user can fix one, and it is served by the host that would otherwise fail to start.
			_logger.Warning(ex, "The public TLS certificate could not be prepared");
			return new PublicTlsBootstrapResult(PublicTlsBootstrapAction.None, false);
		}
	}

	public PublicTlsBootstrapResult ReissueHostCertificate(DateTimeOffset now)
	{
		if (_store.LoadAuthority().Material is not { } authority)
		{
			return Run(keyRingLocked: false, now);
		}

		IssueHostCertificate(authority, now);

		return new PublicTlsBootstrapResult(PublicTlsBootstrapAction.ReissueHostCertificate, true);
	}

	public PublicTlsBootstrapResult RegenerateAuthority(DateTimeOffset now)
	{
		var authority = LocalCertificateAuthority.CreateAuthority(_hostNameProvider.MachineName, now);
		_store.SaveAuthority(authority.CertificatePem, authority.PrivateKeyPem);
		IssueHostCertificate(authority, now);

		_logger.Information("Created a new local certificate authority; every device has to trust it again");

		return new PublicTlsBootstrapResult(PublicTlsBootstrapAction.CreateAuthorityAndHostCertificate, true);
	}

	private PublicTlsCertificateState BuildState(bool keyRingLocked)
	{
		var hostInfo = _store.ReadInfo();
		var authorityInfo = _store.ReadAuthorityInfo();

		if (keyRingLocked || authorityInfo is null)
		{
			return new PublicTlsCertificateState(hostInfo, authorityInfo, false, false, [], keyRingLocked);
		}

		var authorityKeyUsable = _store.LoadAuthority() is { Failure: PublicTlsFailure.None, Material: not null };
		var issuedByAuthority = false;
		IReadOnlyList<IPAddress> uncoveredAddresses = [];

		if (_store.ReadCertificatePem() is { } hostPem && _store.ReadAuthorityCertificatePem() is { } authorityPem)
		{
			using var hostCertificate = X509Certificate2.CreateFromPem(hostPem);
			using var authorityCertificate = X509Certificate2.CreateFromPem(authorityPem);

			issuedByAuthority = LocalCertificateAuthority.IsIssuedBy(hostCertificate, authorityCertificate);
			if (issuedByAuthority)
			{
				uncoveredAddresses = LocalCertificateAuthority.FindUncoveredAddresses(hostCertificate,
					_addressProvider.GetReachableIpv4Addresses());
			}
		}

		return new PublicTlsCertificateState(hostInfo,
			authorityInfo,
			authorityKeyUsable,
			issuedByAuthority,
			uncoveredAddresses,
			keyRingLocked);
	}

	private PublicTlsBootstrapResult Execute(PublicTlsBootstrapAction action, DateTimeOffset now)
	{
		switch (action)
		{
			case PublicTlsBootstrapAction.CreateAuthorityAndHostCertificate:
			{
				var authority = LocalCertificateAuthority.CreateAuthority(_hostNameProvider.MachineName, now);
				_store.SaveAuthority(authority.CertificatePem, authority.PrivateKeyPem);
				IssueHostCertificate(authority, now);

				return new PublicTlsBootstrapResult(action, true);
			}

			case PublicTlsBootstrapAction.ReissueHostCertificate:
			{
				if (_store.LoadAuthority().Material is not { } authority)
				{
					return new PublicTlsBootstrapResult(PublicTlsBootstrapAction.None, false);
				}

				IssueHostCertificate(authority, now);

				return new PublicTlsBootstrapResult(action, true);
			}

			default:
				return new PublicTlsBootstrapResult(PublicTlsBootstrapAction.None, false);
		}
	}

	private void IssueHostCertificate(GeneratedCertificate authority, DateTimeOffset now)
	{
		var host = LocalCertificateAuthority.IssueHostCertificate(authority,
			_addressProvider.GetReachableIpv4Addresses(),
			_hostNameProvider.GetHostNames(),
			now);

		_store.Save(host.CertificatePem, host.PrivateKeyPem, PublicTlsCertificateSource.LocalCa);

		// Swapping the live certificate is what keeps a renewal - after a DHCP change, or before expiry -
		// from needing a restart. Absent during startup, where the listener has not been built yet.
		if (_certificateHolder is not null &&
			_store.LoadServerCertificate() is { Certificate: { } certificate })
		{
			_certificateHolder.Replace(certificate);
		}
	}
}
