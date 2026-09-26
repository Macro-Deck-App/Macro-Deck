using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MacroDeckHost.Application.Configuration;

namespace MacroDeckHost.Tests.UnitTests;

public static class TestListenerPorts
{
	public const int Loopback = 54321;

	public const string LoopbackSecret = "0f1e2d3c4b5a69788796a5b4c3d2e1f000112233445566778899aabbccddeeff";

	// What the bootstrapper mints for its window (ui/bootstrapper/src/loopback_secret.rs session_code).
	public static string SessionCode(string secretHex, DateTimeOffset now, string nonce)
	{
		var expiry = (now + TimeSpan.FromSeconds(60)).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
		var tag = HMACSHA256.HashData(Convert.FromHexString(secretHex),
			Encoding.UTF8.GetBytes($"macro-deck-loopback-code:{expiry}.{nonce}"));
		return $"{expiry}.{nonce}.{Convert.ToHexStringLower(tag)}";
	}
}

[SetUpFixture]
public class ResolvedListenerPortsSetup
{
	[OneTimeSetUp]
	public void PublishLoopbackPort()
	{
		ResolvedLoopbackPort.Set(TestListenerPorts.Loopback);
		global::MacroDeckHost.Application.Configuration.LoopbackSecret.Set(TestListenerPorts.LoopbackSecret);
	}
}
