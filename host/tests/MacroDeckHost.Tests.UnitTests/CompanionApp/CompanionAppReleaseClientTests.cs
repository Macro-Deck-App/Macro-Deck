using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using MacroDeckHost.Application.CompanionApp;
using MacroDeckHost.Infrastructure.CompanionApp;
using MacroDeckHost.Tests.UnitTests.Http;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.CompanionApp;

public class CompanionAppReleaseClientTests
{
	private static readonly byte[] SigningCertificate = ApkSigningCertificatesTests.Certificate(1);
	private static readonly string PinnedCertificate = ApkSigningCertificatesTests.Hex(SigningCertificate);

	private string _directory = null!;

	[SetUp]
	public void SetUp()
	{
		_directory = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_directory);
	}

	[TearDown]
	public void TearDown() => Directory.Delete(_directory, recursive: true);

	[Test]
	public async Task The_published_manifest_is_read()
	{
		var client = Client(new FakeHttpMessageHandler(Encoding.UTF8.GetBytes(Manifest())));

		var release = await client.GetLatestAsync(CancellationToken.None);

		Assert.That(release, Is.EqualTo(new CompanionAppRelease("26.1.0",
			9,
			new Uri("https://packages.macro-deck.app/companion/android/26.1.0/macro-deck-companion-26.1.0.apk"),
			new string('a', 64),
			new DateTimeOffset(2026, 9, 24, 7, 22, 57, TimeSpan.Zero))));
	}

	[TestCase("http://packages.macro-deck.app/companion/android/x.apk")]
	[TestCase("https://evil.example/companion/android/x.apk")]
	[TestCase("https://packages.macro-deck.app:8443/companion/android/x.apk")]
	public async Task A_manifest_pointing_anywhere_but_the_release_host_over_https_is_rejected(string url)
	{
		var client = Client(new FakeHttpMessageHandler(Encoding.UTF8.GetBytes(Manifest(url: url))));

		Assert.That(await client.GetLatestAsync(CancellationToken.None), Is.Null);
	}

	[Test]
	public async Task A_manifest_without_a_valid_checksum_is_rejected()
	{
		var client = Client(new FakeHttpMessageHandler(Encoding.UTF8.GetBytes(Manifest(sha256: "abc"))));

		Assert.That(await client.GetLatestAsync(CancellationToken.None), Is.Null);
	}

	[Test]
	public async Task An_unreachable_manifest_yields_no_release()
	{
		var client = Client(new FakeHttpMessageHandler(status: HttpStatusCode.ServiceUnavailable));

		Assert.That(await client.GetLatestAsync(CancellationToken.None), Is.Null);
	}

	[Test]
	public async Task A_matching_apk_signed_with_the_pinned_certificate_is_kept()
	{
		var apk = ApkSigningCertificatesTests.Apk([SigningCertificate], jarCertificates: [SigningCertificate]);
		var destination = Path.Combine(_directory, "companion.apk");

		var outcome = await Client(new FakeHttpMessageHandler(apk)).DownloadAsync(Release(apk), destination, CancellationToken.None);

		Assert.That(outcome, Is.EqualTo(CompanionApkDownloadOutcome.Downloaded));
		Assert.That(await File.ReadAllBytesAsync(destination), Is.EqualTo(apk));
	}

	[Test]
	public async Task An_apk_that_differs_from_the_manifest_checksum_is_discarded()
	{
		var apk = ApkSigningCertificatesTests.Apk([SigningCertificate]);
		var destination = Path.Combine(_directory, "companion.apk");

		var outcome = await Client(new FakeHttpMessageHandler(apk))
			.DownloadAsync(Release([1, 2, 3]), destination, CancellationToken.None);

		Assert.That(outcome, Is.EqualTo(CompanionApkDownloadOutcome.VerificationFailed));
		Assert.That(Directory.EnumerateFileSystemEntries(_directory), Is.Empty);
	}

	[Test]
	public async Task An_apk_signed_by_another_key_is_discarded_even_when_the_manifest_vouches_for_it()
	{
		var apk = ApkSigningCertificatesTests.Apk([ApkSigningCertificatesTests.Certificate(9)]);
		var destination = Path.Combine(_directory, "companion.apk");

		var outcome = await Client(new FakeHttpMessageHandler(apk)).DownloadAsync(Release(apk), destination, CancellationToken.None);

		Assert.That(outcome, Is.EqualTo(CompanionApkDownloadOutcome.VerificationFailed));
		Assert.That(Directory.EnumerateFileSystemEntries(_directory), Is.Empty);
	}

	[Test]
	public async Task An_apk_whose_jar_signature_uses_another_key_is_discarded_for_android_6()
	{
		var apk = ApkSigningCertificatesTests.Apk([SigningCertificate],
			jarCertificates: [ApkSigningCertificatesTests.Certificate(9)]);

		var outcome = await Client(new FakeHttpMessageHandler(apk))
			.DownloadAsync(Release(apk), Path.Combine(_directory, "companion.apk"), CancellationToken.None);

		Assert.That(outcome, Is.EqualTo(CompanionApkDownloadOutcome.VerificationFailed));
	}

	[Test]
	public async Task An_apk_with_a_second_foreign_signer_is_discarded()
	{
		var apk = ApkSigningCertificatesTests.Apk([SigningCertificate, ApkSigningCertificatesTests.Certificate(9)]);

		var outcome = await Client(new FakeHttpMessageHandler(apk))
			.DownloadAsync(Release(apk), Path.Combine(_directory, "companion.apk"), CancellationToken.None);

		Assert.That(outcome, Is.EqualTo(CompanionApkDownloadOutcome.VerificationFailed));
	}

	[Test]
	public async Task An_oversized_download_is_refused()
	{
		var handler = new FakeHttpMessageHandler([1],
			headers: [("Content-Length", (CompanionAppReleaseClient.MaxApkBytes + 1).ToString(CultureInfo.InvariantCulture))]);

		var outcome = await Client(handler)
			.DownloadAsync(Release([1]), Path.Combine(_directory, "companion.apk"), CancellationToken.None);

		Assert.That(outcome, Is.EqualTo(CompanionApkDownloadOutcome.DownloadFailed));
	}

	private static CompanionAppReleaseClient Client(HttpMessageHandler handler)
		=> new(handler, PinnedCertificate, new LoggerConfiguration().CreateLogger());

	private static CompanionAppRelease Release(byte[] apk)
		=> new("26.1.0",
			9,
			new Uri("https://packages.macro-deck.app/companion/android/26.1.0/macro-deck-companion-26.1.0.apk"),
			Convert.ToHexStringLower(SHA256.HashData(apk)),
			null);

	private static string Manifest(string? url = null, string? sha256 = null)
		=> $$"""
			{
			  "version": "26.1.0",
			  "versionCode": 9,
			  "url": "{{url ?? "https://packages.macro-deck.app/companion/android/26.1.0/macro-deck-companion-26.1.0.apk"}}",
			  "sha256": "{{sha256 ?? new string('A', 64)}}",
			  "signingCertificateSha256": "234a32a93375414d115cf41fb9e7f62d6ed2cbb882cf5b49ce3d1c4f704a27e4",
			  "publishedAt": "2026-09-24T07:22:57Z"
			}
			""";
}
