using System.Net;
using MacroDeckHost.Application.Network.Tls;

namespace MacroDeckHost.Tests.UnitTests.Network.Tls;

[TestFixture]
public class PublicTlsBootstrapPlannerTests
{
	private static readonly DateTimeOffset _now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

	// Every trap at once: uploaded material that is also expired, unrecognised and missing an address.
	// None of that may cause Macro Deck to overwrite a certificate the user supplied.
	[Test]
	public void A_User_Supplied_Certificate_Is_Never_Replaced()
	{
		var state = new PublicTlsCertificateState(Certificate(PublicTlsCertificateSource.Custom, _now.AddYears(-2)),
			null,
			false,
			false,
			[IPAddress.Parse("192.168.1.42")],
			false);

		Assert.That(PublicTlsBootstrapPlanner.Decide(state, _now), Is.EqualTo(PublicTlsBootstrapAction.None));
	}

	[Test]
	public void A_Legacy_Self_Signed_Certificate_Is_Replaced_By_One_The_Authority_Issued()
	{
		var state = new PublicTlsCertificateState(Certificate(PublicTlsCertificateSource.SelfSigned, _now.AddYears(5)),
			null,
			false,
			false,
			[],
			false);

		Assert.That(PublicTlsBootstrapPlanner.Decide(state, _now),
			Is.EqualTo(PublicTlsBootstrapAction.CreateAuthorityAndHostCertificate));
	}

	[Test]
	public void An_Installation_With_No_Certificate_At_All_Gets_An_Authority_And_A_Host_Certificate()
	{
		var state = new PublicTlsCertificateState(null, null, false, false, [], false);

		Assert.That(PublicTlsBootstrapPlanner.Decide(state, _now),
			Is.EqualTo(PublicTlsBootstrapAction.CreateAuthorityAndHostCertificate));
	}

	[Test]
	public void A_Healthy_Certificate_Issued_By_The_Authority_Is_Left_Alone()
	{
		Assert.That(PublicTlsBootstrapPlanner.Decide(Healthy(), _now), Is.EqualTo(PublicTlsBootstrapAction.None));
	}

	// The dangerous case: reissuing is correct, minting a second authority would invalidate the trust
	// every paired device already granted.
	[Test]
	public void An_Expired_Certificate_From_The_Authority_Is_Reissued_Without_A_New_Authority()
	{
		var state = Healthy() with
		{
			HostCertificate = Certificate(PublicTlsCertificateSource.LocalCa, _now.AddDays(-1))
		};

		Assert.That(PublicTlsBootstrapPlanner.Decide(state, _now),
			Is.EqualTo(PublicTlsBootstrapAction.ReissueHostCertificate));
	}

	[Test]
	public void A_Certificate_Close_To_Expiry_Is_Reissued()
	{
		var state = Healthy() with
		{
			HostCertificate = Certificate(PublicTlsCertificateSource.LocalCa, _now.AddDays(10))
		};

		Assert.That(PublicTlsBootstrapPlanner.Decide(state, _now),
			Is.EqualTo(PublicTlsBootstrapAction.ReissueHostCertificate));
	}

	// A transient key ring failure must never read as "there is no authority".
	[Test]
	public void An_Authority_Whose_Key_Cannot_Be_Opened_Never_Produces_A_Second_Authority()
	{
		var state = Healthy() with { AuthorityKeyUsable = false };

		Assert.That(PublicTlsBootstrapPlanner.Decide(state, _now), Is.EqualTo(PublicTlsBootstrapAction.None));
	}

	[Test]
	public void A_Locked_Key_Ring_Changes_Nothing()
	{
		var state = new PublicTlsCertificateState(null, null, false, false, [], true);

		Assert.That(PublicTlsBootstrapPlanner.Decide(state, _now), Is.EqualTo(PublicTlsBootstrapAction.None));
	}

	[Test]
	public void A_Certificate_That_Does_Not_Cover_A_Current_Address_Is_Reissued()
	{
		var state = Healthy() with { UncoveredAddresses = [IPAddress.Parse("192.168.1.42")] };

		Assert.That(PublicTlsBootstrapPlanner.Decide(state, _now),
			Is.EqualTo(PublicTlsBootstrapAction.ReissueHostCertificate));
	}

	[Test]
	public void A_Certificate_Left_Over_From_A_Previous_Authority_Is_Reissued()
	{
		var state = Healthy() with { HostCertificateIssuedByAuthority = false };

		Assert.That(PublicTlsBootstrapPlanner.Decide(state, _now),
			Is.EqualTo(PublicTlsBootstrapAction.ReissueHostCertificate));
	}

	private static PublicTlsCertificateState Healthy()
	{
		return new PublicTlsCertificateState(Certificate(PublicTlsCertificateSource.LocalCa, _now.AddDays(300)),
			Certificate(PublicTlsCertificateSource.LocalCa, _now.AddYears(9)),
			true,
			true,
			[],
			false);
	}

	private static PublicTlsCertificateInfo Certificate(PublicTlsCertificateSource source, DateTimeOffset notAfter)
	{
		return new PublicTlsCertificateInfo("CN=test", "FINGERPRINT", _now.AddDays(-1), notAfter, source);
	}
}
