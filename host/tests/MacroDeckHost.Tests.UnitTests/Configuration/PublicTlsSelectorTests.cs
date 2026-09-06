using MacroDeckHost.Application.Configuration;

namespace MacroDeckHost.Tests.UnitTests.Configuration;

public class PublicTlsSelectorTests
{
	private const int PublicPort = 8193;
	private const int HttpsPort = 8194;
	private const int LoopbackPort = 54321;

	private static PublicTlsSelection Resolve(string? enabled,
		string? mode = "Additional",
		string? httpsPort = "8194",
		bool certificateConfigured = true)
		=> PublicTlsSelector.Resolve(enabled, mode, httpsPort, certificateConfigured, PublicPort, LoopbackPort);

	// A browser only grants a secure context, and with it the service worker the web client installs
	// from, over HTTPS. An installation that never opened the network settings has to get one anyway,
	// and a stored value that cannot be read carries no user intent either.
	[Test]
	public void Https_is_on_unless_the_user_turned_it_off()
	{
		foreach (var stored in (string?[])[null, "", "nonsense"])
		{
			var selection = Resolve(stored);

			Assert.Multiple(() =>
			{
				Assert.That(selection.Endpoints.HttpsPort,
					Is.EqualTo(HttpsPort),
					$"stored: {stored ?? "<null>"}");
				Assert.That(selection.Endpoints.HttpPort, Is.EqualTo(PublicPort));
				Assert.That(selection.Rejection, Is.EqualTo(PublicTlsRejection.None));
			});
		}
	}

	[Test]
	public void Https_stays_off_when_the_user_turned_it_off()
	{
		var selection = Resolve("False");

		Assert.Multiple(() =>
		{
			Assert.That(selection.Endpoints.HttpsPort, Is.Null);
			Assert.That(selection.Endpoints.HttpPort, Is.EqualTo(PublicPort));
			Assert.That(selection.Rejection, Is.EqualTo(PublicTlsRejection.None));
		});
	}

	[Test]
	public void Additional_mode_opens_https_on_its_own_port_alongside_http()
	{
		var selection = Resolve("True");

		Assert.Multiple(() =>
		{
			Assert.That(selection.Endpoints.HttpPort, Is.EqualTo(PublicPort));
			Assert.That(selection.Endpoints.HttpsPort, Is.EqualTo(HttpsPort));
			Assert.That(selection.Rejection, Is.EqualTo(PublicTlsRejection.None));
		});
	}

	[Test]
	public void Replace_mode_serves_https_on_the_public_port_and_ignores_the_stored_https_port()
	{
		var selection = Resolve("True", mode: "Replace", httpsPort: "80");

		Assert.Multiple(() =>
		{
			Assert.That(selection.Endpoints.HttpPort, Is.Null);
			Assert.That(selection.Endpoints.HttpsPort, Is.EqualTo(PublicPort));
			Assert.That(selection.Rejection, Is.EqualTo(PublicTlsRejection.None));
		});
	}

	// Turning HTTPS on with nothing to serve it with must not quietly leave the port unencrypted; it
	// is reported so the settings screen can say why HTTPS is not running.
	[Test]
	public void Https_without_a_certificate_is_refused_and_reported()
	{
		var selection = Resolve("True", certificateConfigured: false);

		Assert.Multiple(() =>
		{
			Assert.That(selection.Endpoints.HttpsPort, Is.Null);
			Assert.That(selection.Endpoints.HttpPort, Is.EqualTo(PublicPort));
			Assert.That(selection.Rejection, Is.EqualTo(PublicTlsRejection.NoCertificate));
		});
	}

	[TestCase("8193", PublicTlsRejection.HttpsPortConflictsWithPublicPort)]
	[TestCase("54321", PublicTlsRejection.HttpsPortConflictsWithLoopbackPort)]
	public void An_https_port_that_collides_with_another_listener_is_refused_without_disturbing_http(string stored,
		PublicTlsRejection expected)
	{
		var selection = Resolve("True", httpsPort: stored);

		Assert.Multiple(() =>
		{
			Assert.That(selection.Rejection, Is.EqualTo(expected));
			Assert.That(selection.Endpoints.HttpsPort, Is.Null);
			Assert.That(selection.Endpoints.HttpPort, Is.EqualTo(PublicPort));
		});
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("nonsense")]
	[TestCase("0")]
	[TestCase("80")]
	[TestCase("1023")]
	[TestCase("65536")]
	public void An_absent_or_unusable_stored_https_port_falls_back_to_the_build_default(string? stored)
	{
		var selection = Resolve("True", httpsPort: stored);

		Assert.Multiple(() =>
		{
			Assert.That(selection.Rejection, Is.EqualTo(PublicTlsRejection.None));
			Assert.That(selection.Endpoints.HttpsPort, Is.EqualTo(BuildConfig.DefaultPublicHttpsPort));
			Assert.That(selection.Endpoints.HttpPort, Is.EqualTo(PublicPort));
		});
	}

	[Test]
	public void The_lowest_and_highest_configurable_https_ports_are_accepted()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Resolve("True", httpsPort: "1024").Endpoints.HttpsPort, Is.EqualTo(1024));
			Assert.That(Resolve("True", httpsPort: "65535").Endpoints.HttpsPort, Is.EqualTo(65535));
		});
	}

	[Test]
	public void An_unrecognised_mode_keeps_the_public_http_listener()
	{
		var selection = Resolve("True", mode: "nonsense");

		Assert.Multiple(() =>
		{
			Assert.That(selection.Endpoints.HttpPort, Is.EqualTo(PublicPort));
			Assert.That(selection.Endpoints.HttpsPort, Is.EqualTo(HttpsPort));
		});
	}
}
