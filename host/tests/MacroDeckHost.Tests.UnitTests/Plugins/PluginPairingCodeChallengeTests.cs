using MacroDeckHost.Application.Plugins.Pairing;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
public class PluginPairingCodeChallengeTests
{
	// RFC 7636 Appendix B's worked example: https://www.rfc-editor.org/rfc/rfc7636#appendix-B. Every
	// other test in this suite (host, SDK, MacroDeckTestHost, FakePluginHost) independently *recomputes*
	// base64url(SHA-256(verifier)) rather than asserting a literal, so a shared misunderstanding of S256
	// would pass the whole suite while breaking interop with a spec-compliant client. This is the one
	// test that pins the host's implementation to the standard's own known answer.
	[Test]
	public void Matches_the_RFC_7636_appendix_B_known_answer()
	{
		const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
		const string expectedChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

		Assert.That(PluginPairingCodeChallenge.Verify(verifier, expectedChallenge), Is.True);
	}
}
