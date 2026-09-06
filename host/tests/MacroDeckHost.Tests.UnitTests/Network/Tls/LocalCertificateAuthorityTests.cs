using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MacroDeckHost.Application.Network.Tls;

namespace MacroDeckHost.Tests.UnitTests.Network.Tls;

[TestFixture]
public class LocalCertificateAuthorityTests
{
	private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";

	private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

	[Test]
	public void The_Authority_Can_Sign_Certificates_But_Cannot_Create_Further_Authorities()
	{
		using var authority = LoadAuthority();

		var basicConstraints = authority.Extensions.OfType<X509BasicConstraintsExtension>().Single();
		var keyUsage = authority.Extensions.OfType<X509KeyUsageExtension>().Single();

		Assert.Multiple(() =>
		{
			Assert.That(basicConstraints.CertificateAuthority, Is.True);
			Assert.That(basicConstraints.HasPathLengthConstraint, Is.True);
			Assert.That(basicConstraints.PathLengthConstraint, Is.Zero);
			Assert.That(keyUsage.KeyUsages.HasFlag(X509KeyUsageFlags.KeyCertSign), Is.True);
			Assert.That(authority.Extensions.OfType<X509EnhancedKeyUsageExtension>(), Is.Empty);
		});
	}

	[Test]
	public void The_Host_Certificate_Chains_To_The_Authority_As_A_Server_End_Entity()
	{
		var (authority, authorityCertificate, hostCertificate) = IssueChain();
		using (authority)
		using (authorityCertificate)
		using (hostCertificate)
		{
			var basicConstraints = hostCertificate.Extensions.OfType<X509BasicConstraintsExtension>().Single();
			var enhancedKeyUsage = hostCertificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().Single();

			Assert.Multiple(() =>
			{
				Assert.That(LocalCertificateAuthority.IsIssuedBy(hostCertificate, authorityCertificate), Is.True);
				Assert.That(basicConstraints.CertificateAuthority, Is.False);
				Assert.That(enhancedKeyUsage.EnhancedKeyUsages.Cast<Oid>().Select(oid => oid.Value),
					Does.Contain(ServerAuthenticationOid));
			});
		}
	}

	[Test]
	public void The_Host_Certificate_Names_Every_Address_A_Client_Can_Reach_The_Host_On()
	{
		IPAddress[] addresses = [IPAddress.Parse("192.168.1.42"), IPAddress.Parse("10.0.0.7")];
		var authority = LocalCertificateAuthority.CreateAuthority("deck-pc", Now);
		var host = LocalCertificateAuthority.IssueHostCertificate(authority,
			addresses,
			["deck-pc", "deck-pc.local"],
			Now);
		using var hostCertificate = X509Certificate2.CreateFromPem(host.CertificatePem);

		var names = LocalCertificateAuthority.ReadSubjectAlternativeNames(hostCertificate);

		Assert.Multiple(() =>
		{
			Assert.That(names, Does.Contain("localhost"));
			Assert.That(names, Does.Contain("deck-pc"));
			Assert.That(names, Does.Contain("deck-pc.local"));
			Assert.That(names, Does.Contain(IPAddress.Loopback.ToString()));
			Assert.That(names, Does.Contain(IPAddress.IPv6Loopback.ToString()));
			foreach (var address in addresses)
			{
				Assert.That(names, Does.Contain(address.ToString()));
			}
		});
	}

	// The point of the whole authority: a host certificate can be replaced without any device having to
	// install and trust anything again.
	[Test]
	public void Reissuing_The_Host_Certificate_Keeps_The_Same_Authority()
	{
		var authority = LocalCertificateAuthority.CreateAuthority("deck-pc", Now);
		using var authorityCertificate = X509Certificate2.CreateFromPem(authority.CertificatePem);

		var first = LocalCertificateAuthority.IssueHostCertificate(authority,
			[IPAddress.Parse("192.168.1.42")],
			[],
			Now);
		var second = LocalCertificateAuthority.IssueHostCertificate(authority,
			[IPAddress.Parse("192.168.1.99")],
			[],
			Now.AddDays(200));

		using var firstCertificate = X509Certificate2.CreateFromPem(first.CertificatePem);
		using var secondCertificate = X509Certificate2.CreateFromPem(second.CertificatePem);

		Assert.Multiple(() =>
		{
			Assert.That(LocalCertificateAuthority.IsIssuedBy(firstCertificate, authorityCertificate), Is.True);
			Assert.That(LocalCertificateAuthority.IsIssuedBy(secondCertificate, authorityCertificate), Is.True);
			Assert.That(secondCertificate.Thumbprint, Is.Not.EqualTo(firstCertificate.Thumbprint));
			Assert.That(secondCertificate.SerialNumber, Is.Not.EqualTo(firstCertificate.SerialNumber));
		});
	}

	[Test]
	public void The_Host_Certificate_Never_Outlives_The_Authority()
	{
		var (authority, authorityCertificate, hostCertificate) = IssueChain();
		using (authority)
		using (authorityCertificate)
		using (hostCertificate)
		{
			Assert.That(hostCertificate.NotAfter, Is.LessThanOrEqualTo(authorityCertificate.NotAfter));
		}
	}

	// Guards the verification flags in IsIssuedBy: an expired certificate that reads as "not ours" would
	// make the bootstrap planner mint a second authority and silently break every paired device.
	[Test]
	public void An_Expired_Host_Certificate_Is_Still_Recognised_As_Issued_By_The_Authority()
	{
		var longAgo = Now.AddYears(-3);
		var authority = LocalCertificateAuthority.CreateAuthority("deck-pc", longAgo);
		var host = LocalCertificateAuthority.IssueHostCertificate(authority, [], [], longAgo);

		using var authorityCertificate = X509Certificate2.CreateFromPem(authority.CertificatePem);
		using var hostCertificate = X509Certificate2.CreateFromPem(host.CertificatePem);

		Assert.Multiple(() =>
		{
			Assert.That(hostCertificate.NotAfter, Is.LessThan(DateTime.Now));
			Assert.That(LocalCertificateAuthority.IsIssuedBy(hostCertificate, authorityCertificate), Is.True);
		});
	}

	[Test]
	public void A_Certificate_From_Another_Authority_Is_Not_Recognised()
	{
		var authority = LocalCertificateAuthority.CreateAuthority("deck-pc", Now);
		var otherAuthority = LocalCertificateAuthority.CreateAuthority("someone-else", Now);
		var host = LocalCertificateAuthority.IssueHostCertificate(otherAuthority, [], [], Now);

		using var authorityCertificate = X509Certificate2.CreateFromPem(authority.CertificatePem);
		using var hostCertificate = X509Certificate2.CreateFromPem(host.CertificatePem);

		Assert.That(LocalCertificateAuthority.IsIssuedBy(hostCertificate, authorityCertificate), Is.False);
	}

	[Test]
	public void An_Address_The_Certificate_Does_Not_Name_Is_Reported_As_Uncovered()
	{
		var authority = LocalCertificateAuthority.CreateAuthority("deck-pc", Now);
		var host = LocalCertificateAuthority.IssueHostCertificate(authority,
			[IPAddress.Parse("192.168.1.42")],
			[],
			Now);
		using var hostCertificate = X509Certificate2.CreateFromPem(host.CertificatePem);

		var uncovered = LocalCertificateAuthority.FindUncoveredAddresses(hostCertificate,
			[IPAddress.Parse("192.168.1.42"), IPAddress.Parse("10.0.0.7")]);

		Assert.That(uncovered, Is.EqualTo(new[] { IPAddress.Parse("10.0.0.7") }));
	}

	private static X509Certificate2 LoadAuthority()
		=> X509Certificate2.CreateFromPem(LocalCertificateAuthority.CreateAuthority("deck-pc", Now).CertificatePem);

	private static (X509Certificate2 Authority, X509Certificate2 AuthorityCertificate, X509Certificate2 Host)
		IssueChain()
	{
		var authority = LocalCertificateAuthority.CreateAuthority("deck-pc", Now);
		var host = LocalCertificateAuthority.IssueHostCertificate(authority,
			[IPAddress.Parse("192.168.1.42")],
			[],
			Now);

		return (X509Certificate2.CreateFromPem(authority.CertificatePem),
			X509Certificate2.CreateFromPem(authority.CertificatePem),
			X509Certificate2.CreateFromPem(host.CertificatePem));
	}
}
