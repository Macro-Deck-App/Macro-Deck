using System.Net;
using System.Security.Cryptography;
using MacroDeckHost.Application.Network.Tls;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Network.Tls;

public class PublicTlsBootstrapperTests
{
	private static readonly DateTimeOffset _now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

	[Test]
	public void An_unreachable_key_isolation_service_is_reported_so_the_app_can_name_it()
	{
		var store = new FakePublicTlsCertificateStore
		{
			SaveAuthorityFailure = new CryptographicException("no endpoints") { HResult = unchecked((int)0x800706D9) }
		};

		var result = Bootstrapper(store).Run(false, _now);

		Assert.Multiple(() =>
		{
			Assert.That(result.CryptographyUnavailable, Is.True);
			Assert.That(store.HasCertificate, Is.False);
		});
	}

	[Test]
	public void Any_other_certificate_failure_is_not_reported_as_an_unavailable_service()
	{
		var store = new FakePublicTlsCertificateStore
		{
			SaveAuthorityFailure = new CryptographicException("bad key")
		};

		Assert.That(Bootstrapper(store).Run(false, _now).CryptographyUnavailable, Is.False);
	}

	private static PublicTlsBootstrapper Bootstrapper(FakePublicTlsCertificateStore store)
	{
		return new PublicTlsBootstrapper(store, new NoAddresses(), new FixedHostName(), Logger.None);
	}

	private sealed class NoAddresses : ILocalAddressProvider
	{
		public IReadOnlyList<IPAddress> GetReachableIpv4Addresses()
		{
			return [];
		}
	}

	private sealed class FixedHostName : IHostNameProvider
	{
		public string MachineName => "deck-pc";

		public IReadOnlyList<string> GetHostNames()
		{
			return ["deck-pc"];
		}
	}
}
