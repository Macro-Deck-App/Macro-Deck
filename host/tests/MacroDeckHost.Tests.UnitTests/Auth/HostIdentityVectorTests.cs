using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MacroDeckHost.Auth;

namespace MacroDeckHost.Tests.UnitTests.Auth;

// host-identity-vector.json was produced with the openssl command line, independently of the host, and the
// companion app checks its verifier against the same file.
[TestFixture]
public class HostIdentityVectorTests
{
	private static readonly JsonElement Vector = JsonDocument.Parse(File.ReadAllText(
			Path.Combine(TestContext.CurrentContext.TestDirectory, "Auth", "host-identity-vector.json")))
		.RootElement;

	private static string Field(string name) => Vector.GetProperty(name).GetString()!;

	[Test]
	public void The_message_is_the_domain_line_key_endpoint_and_nonce_joined_by_newlines()
		=> Assert.That(Field("message"),
			Is.EqualTo($"macrodeck-host-identity/v1\n{Field("publicKey")}\n{Field("endpoint")}\n{Field("nonce")}"));

	[Test]
	public void The_vector_verifies_and_any_changed_field_does_not()
	{
		var tamperedEndpoint = Field("message").Replace(Field("endpoint"), "192.168.1.20:8194", StringComparison.Ordinal);

		Assert.Multiple(() =>
		{
			Assert.That(Verifies(Field("message")), Is.True);
			Assert.That(Verifies(tamperedEndpoint), Is.False);
			Assert.That(Verifies(Field("message").Replace("/v1", "/v2", StringComparison.Ordinal)), Is.False);
		});
	}

	[Test]
	public void The_host_formats_the_vector_endpoint_and_accepts_its_nonce()
	{
		Assert.Multiple(() =>
		{
			Assert.That(HostIdentityMessage.CanonicalAuthority(IPAddress.Parse("192.168.1.20"), 8193),
				Is.EqualTo(Field("endpoint")));
			Assert.That(HostIdentityMessage.TryParseNonce(Field("nonce"), out _), Is.True);
			Assert.That(Encoding.UTF8.GetString(HostIdentityMessage.Build(Field("publicKey"),
					Field("endpoint"),
					Field("nonce"))),
				Is.EqualTo(Field("message")));
		});
	}

	[Test]
	public void The_fingerprint_is_the_first_twelve_hash_bytes_in_six_groups()
		=> Assert.That(HostIdentityMessage.Fingerprint(Convert.FromBase64String(Field("publicKey"))),
			Is.EqualTo(Field("fingerprint")));

	[Test]
	public void The_connect_payload_fingerprint_is_the_same_value_without_spaces()
		=> Assert.That(Field("fingerprint").Replace(" ", string.Empty, StringComparison.Ordinal),
			Is.EqualTo(Field("compactFingerprint")).And.Match("^[0-9A-F]{24}$"));

	private static bool Verifies(string message)
	{
		var point = Convert.FromBase64String(Field("publicKey"));
		using var key = ECDsa.Create(new ECParameters
		{
			Curve = ECCurve.NamedCurves.nistP256,
			Q = new ECPoint { X = point[1..33], Y = point[33..] }
		});

		return key.VerifyData(Encoding.UTF8.GetBytes(message),
			Convert.FromBase64String(Field("signature")),
			HashAlgorithmName.SHA256,
			DSASignatureFormat.Rfc3279DerSequence);
	}
}
