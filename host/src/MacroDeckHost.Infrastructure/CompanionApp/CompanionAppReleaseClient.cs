using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using MacroDeckHost.Application.CompanionApp;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.CompanionApp;

public sealed partial class CompanionAppReleaseClient : ICompanionAppReleaseClient, IDisposable
{
	public const string HttpClientName = "companion-app-releases";
	public const string ReleaseHost = "packages.macro-deck.app";
	public const long MaxApkBytes = 64 * 1024 * 1024;

	// The signer is pinned here rather than read from the manifest, so a tampered bucket cannot
	// ship an APK from a different key. Android then verifies the signature against it at install.
	public const string SigningCertificateSha256 = "234a32a93375414d115cf41fb9e7f62d6ed2cbb882cf5b49ce3d1c4f704a27e4";

	public static readonly Uri ManifestUrl = new($"https://{ReleaseHost}/companion/android/latest.json");

	private static readonly TimeSpan ManifestTimeout = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(5);
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private readonly HttpClient _http;
	private readonly bool _ownsClient;
	private readonly string _pinnedCertificate;
	private readonly ILogger _logger;

	public CompanionAppReleaseClient(IHttpClientFactory httpClientFactory, ILogger logger)
		: this(httpClientFactory.CreateClient(HttpClientName), false, SigningCertificateSha256, logger)
	{
	}

	internal CompanionAppReleaseClient(HttpMessageHandler handler, string pinnedCertificate, ILogger logger)
		: this(new HttpClient(handler, disposeHandler: false), true, pinnedCertificate, logger)
	{
	}

	private CompanionAppReleaseClient(HttpClient http, bool ownsClient, string pinnedCertificate, ILogger logger)
	{
		_http = http;
		_ownsClient = ownsClient;
		_pinnedCertificate = pinnedCertificate;
		_logger = logger.ForContext<CompanionAppReleaseClient>();
	}

	public async Task<CompanionAppRelease?> GetLatestAsync(CancellationToken cancellationToken)
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(ManifestTimeout);
		Manifest? manifest;
		try
		{
			using var response = await _http.GetAsync(ManifestUrl, timeout.Token);
			if (!response.IsSuccessStatusCode)
			{
				_logger.Warning("The Companion app release manifest answered {StatusCode}", (int)response.StatusCode);
				return null;
			}

			manifest = await response.Content.ReadFromJsonAsync<Manifest>(Json, timeout.Token);
		}
		catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException ||
			(ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
		{
			_logger.Warning(ex, "Could not read the Companion app release manifest");
			return null;
		}

		var release = Validate(manifest);
		if (release is null)
		{
			_logger.Warning("The Companion app release manifest was rejected as invalid");
		}

		return release;
	}

	public async Task<CompanionApkDownloadOutcome> DownloadAsync(CompanionAppRelease release,
		string destinationPath,
		CancellationToken cancellationToken)
	{
		var partialPath = destinationPath + ".partial";
		try
		{
			var digest = await DownloadToAsync(release.Url, partialPath, cancellationToken);
			if (digest is null)
			{
				return CompanionApkDownloadOutcome.DownloadFailed;
			}

			if (!string.Equals(digest, release.Sha256, StringComparison.OrdinalIgnoreCase))
			{
				_logger.Warning("The Companion app APK {Version} does not match the manifest's SHA-256", release.Version);
				return CompanionApkDownloadOutcome.VerificationFailed;
			}

			IReadOnlyList<string> signers;
			IReadOnlyList<string>? jarSigners;
			await using (var apk = File.OpenRead(partialPath))
			{
				signers = ApkSigningCertificates.ReadSignerCertificateSha256(apk);
				jarSigners = ApkSigningCertificates.ReadJarSignerCertificateSha256(apk);
			}

			if (signers.Count == 0 || jarSigners is null || !signers.Concat(jarSigners).All(IsPinned))
			{
				_logger.Warning("The Companion app APK {Version} is not signed with the expected certificate", release.Version);
				return CompanionApkDownloadOutcome.VerificationFailed;
			}

			File.Move(partialPath, destinationPath, overwrite: true);
			return CompanionApkDownloadOutcome.Downloaded;
		}
		finally
		{
			TryDelete(partialPath);
		}
	}

	private bool IsPinned(string certificate)
		=> string.Equals(certificate, _pinnedCertificate, StringComparison.OrdinalIgnoreCase);

	public void Dispose()
	{
		if (_ownsClient)
		{
			_http.Dispose();
		}
	}

	internal static CompanionAppRelease? Validate(Manifest? manifest)
	{
		if (manifest is null ||
			string.IsNullOrWhiteSpace(manifest.Version) ||
			!VersionRegex().IsMatch(manifest.Version) ||
			manifest.VersionCode <= 0 ||
			manifest.Sha256 is null ||
			!Sha256Regex().IsMatch(manifest.Sha256) ||
			!Uri.TryCreate(manifest.Url, UriKind.Absolute, out var url) ||
			url.Scheme != Uri.UriSchemeHttps ||
			!string.Equals(url.Host, ReleaseHost, StringComparison.OrdinalIgnoreCase) ||
			!url.IsDefaultPort)
		{
			return null;
		}

		return new CompanionAppRelease(manifest.Version,
			manifest.VersionCode,
			url,
			manifest.Sha256.ToLowerInvariant(),
			manifest.PublishedAt);
	}

	private async Task<string?> DownloadToAsync(Uri url, string path, CancellationToken cancellationToken)
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(DownloadTimeout);
		try
		{
			using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
			if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxApkBytes)
			{
				_logger.Warning("The Companion app APK download answered {StatusCode} with {Length} bytes",
					(int)response.StatusCode,
					response.Content.Headers.ContentLength);
				return null;
			}

			await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
			await using var target = File.Create(path);
			using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
			var buffer = new byte[81920];
			long total = 0;
			int read;
			while ((read = await source.ReadAsync(buffer, timeout.Token)) > 0)
			{
				total += read;
				if (total > MaxApkBytes)
				{
					_logger.Warning("The Companion app APK download exceeded {Limit} bytes", MaxApkBytes);
					return null;
				}

				hash.AppendData(buffer, 0, read);
				await target.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
			}

			return Convert.ToHexStringLower(hash.GetHashAndReset());
		}
		catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException ||
			(ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
		{
			_logger.Warning(ex, "Could not download the Companion app APK");
			return null;
		}
	}

	private static void TryDelete(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}

	[GeneratedRegex("^[0-9a-fA-F]{64}$")]
	private static partial Regex Sha256Regex();

	[GeneratedRegex(@"^\d+(\.\d+){0,3}$")]
	private static partial Regex VersionRegex();

	internal sealed class Manifest
	{
		public string? Version { get; init; }

		public int VersionCode { get; init; }

		public string? Url { get; init; }

		public string? Sha256 { get; init; }

		public DateTimeOffset? PublishedAt { get; init; }
	}
}
