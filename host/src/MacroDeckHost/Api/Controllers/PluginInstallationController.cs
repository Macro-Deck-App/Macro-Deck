using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeckHost.Api.Support;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Plugins.Installation;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Application.Plugins.Trust;
using MacroDeckHost.Application.Ui.Transport.Messages;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

public record PluginInstallWarningBody(string Code, string Severity, string Message, string? SubjectId);

public record PluginPublisherBody(string Name, string? Id, string? Url);

public record PluginSignatureBody(
	string Verification,
	string? Message,
	string? Algorithm = null,
	string? KeyId = null,
	string? Category = null,
	string? CertificateId = null,
	bool? Trusted = null);

public record PluginArtifactBody(
	string Name,
	string? Description,
	string? IconDataUri,
	bool SupportedOnThisPlatform);

public record PluginInstallActionResponse(
	bool Success,
	string? PluginId,
	string? Version,
	string? PreviousVersion,
	bool Activated,
	bool RolledBack,
	IReadOnlyList<PluginInstallWarningBody> Warnings,
	TransportError? Error,
	PluginPublisherBody? Publisher = null,
	PluginSignatureBody? Signature = null,
	PluginArtifactBody? Artifact = null);

public record InstalledPluginVersionBody(string Version, bool Active);

public record InstalledPluginBody(
	string PluginId,
	string Name,
	string? Description,
	string? PublisherName,
	string? License,
	string? Homepage,
	IReadOnlyList<InstalledPluginVersionBody> Versions,
	string? ActiveVersion,
	IReadOnlyList<string> Permissions,
	string? TrustVerdict,
	string? CertificateId);

public record GetInstalledPluginsResponse(IReadOnlyList<InstalledPluginBody> Plugins);

public sealed class InstallPluginPathRequest
{
	public string Path { get; set; } = string.Empty;

	public bool Force { get; set; }

	public bool AllowUnsigned { get; set; }
}

public sealed class InstallPluginUrlRequest
{
	public string Url { get; set; } = string.Empty;

	public string? Sha256 { get; set; }

	public bool RetainDownload { get; set; }

	public bool Force { get; set; }
}

public sealed class ActivatePluginVersionRequest
{
	public string Version { get; set; } = string.Empty;
}

[ApiController]
[Route("api/plugin-installation")]
public class PluginInstallationController : ControllerBase
{
	private readonly IPluginInstaller _installer;
	private readonly IPluginInstallationCatalog _catalog;
	private readonly IPluginManifestReader _manifestReader;
	private readonly IPluginArtifactCache _cache;
	private readonly IPluginTrustRecordRepository _trustRecords;

	public PluginInstallationController(IPluginInstaller installer,
		IPluginInstallationCatalog catalog,
		IPluginManifestReader manifestReader,
		IPluginArtifactCache cache,
		IPluginTrustRecordRepository trustRecords)
	{
		_installer = installer;
		_catalog = catalog;
		_manifestReader = manifestReader;
		_cache = cache;
		_trustRecords = trustRecords;
	}

	[HttpGet]
	public async Task<GetInstalledPluginsResponse> GetAll()
	{
		var bodies = new List<InstalledPluginBody>();
		foreach (var plugin in _catalog.Discover())
		{
			bodies.Add(await ToBody(plugin));
		}

		return new GetInstalledPluginsResponse(bodies);
	}

	[HttpPost("inspect")]
	[RequestSizeLimit(PluginArtifactLimits.MaxArchiveBytes)]
	[RequestFormLimits(MultipartBodyLengthLimit = PluginArtifactLimits.MaxArchiveBytes)]
	public async Task<PluginInstallActionResponse> Inspect([FromForm] IFormFile? file, CancellationToken ct)
	{
		if (file is null)
		{
			return Failure("no_artifact", "No artifact was uploaded.");
		}

		await using var stream = file.OpenReadStream();
		return ToResponse(await _installer.Inspect(PluginArtifactSource.FromUpload(stream), ct));
	}

	[HttpPost("install")]
	[RequestSizeLimit(PluginArtifactLimits.MaxArchiveBytes)]
	[RequestFormLimits(MultipartBodyLengthLimit = PluginArtifactLimits.MaxArchiveBytes)]
	public async Task<PluginInstallActionResponse> Install([FromForm] IFormFile? file,
		CancellationToken ct,
		[FromQuery] bool force = false,
		[FromQuery] bool allowUnsigned = false)
	{
		if (file is null)
		{
			return Failure("no_artifact", "No artifact was uploaded.");
		}

		await using var stream = file.OpenReadStream();
		return ToResponse(await _installer.Install(PluginArtifactSource.FromUpload(stream),
			new PluginInstallRequest { Force = force, AllowUnsigned = allowUnsigned },
			ct));
	}

	[HttpPost("inspect-path")]
	public async Task<PluginInstallActionResponse> InspectPath(InstallPluginPathRequest body,
		CancellationToken ct)
	{
		var refusal = PortabilityHttp.RefuseUnlessDesktop(HttpContext);
		if (refusal is not null)
		{
			return new PluginInstallActionResponse(false, null, null, null, false, false, [], refusal);
		}

		return ToResponse(await _installer.Inspect(PluginArtifactSource.FromPath(body.Path), ct));
	}

	[HttpPost("install-path")]
	public async Task<PluginInstallActionResponse> InstallFromPath(InstallPluginPathRequest body,
		CancellationToken ct)
	{
		var refusal = PortabilityHttp.RefuseUnlessDesktop(HttpContext);
		if (refusal is not null)
		{
			return new PluginInstallActionResponse(false, null, null, null, false, false, [], refusal);
		}

		return ToResponse(await _installer.Install(PluginArtifactSource.FromPath(body.Path),
			new PluginInstallRequest { Force = body.Force, AllowUnsigned = body.AllowUnsigned },
			ct));
	}

	[HttpPost("install-url")]
	public async Task<PluginInstallActionResponse> InstallFromUrl(InstallPluginUrlRequest body,
		CancellationToken ct)
	{
		if (!Uri.TryCreate(body.Url, UriKind.Absolute, out var url))
		{
			return Failure("invalid_archive", "The download URL is not a valid absolute URL.");
		}

		var request = new PluginInstallRequest
		{
			RetainDownload = body.RetainDownload,
			Force = body.Force
		};
		return ToResponse(await _installer.Install(PluginArtifactSource.FromUrl(url, body.Sha256),
			request,
			ct));
	}

	[HttpPost("{pluginId}/activate")]
	public async Task<PluginInstallActionResponse> Activate(string pluginId,
		ActivatePluginVersionRequest body,
		CancellationToken ct)
	{
		return ToResponse(await _installer.Activate(pluginId, body.Version, ct));
	}

	[HttpDelete("{pluginId}")]
	public async Task<PluginInstallActionResponse> Uninstall(string pluginId,
		CancellationToken ct,
		[FromQuery] bool keepData = true,
		[FromQuery] bool force = false)
	{
		var request = new PluginUninstallRequest { KeepData = keepData, Force = force };
		return ToResponse(await _installer.Uninstall(pluginId, request, ct));
	}

	[HttpDelete("cache")]
	public PluginInstallActionResponse ClearCache()
	{
		_cache.Clear();
		return new PluginInstallActionResponse(true, null, null, null, false, false, [], null);
	}

	private async Task<InstalledPluginBody> ToBody(InstalledPlugin plugin)
	{
		var manifest = plugin.ActiveVersion is { } active
			? _manifestReader.Read(active.ManifestPath, plugin.PluginId, active.Version).Manifest
			: null;

		var versions = plugin.Versions
			.Select(version => new InstalledPluginVersionBody(version.Version,
				string.Equals(version.Version, plugin.ActiveVersion?.Version, StringComparison.Ordinal)))
			.ToList();

		var trustRecord = plugin.ActiveVersion is { } activeVersion
			? await _trustRecords.GetVersion(plugin.PluginId, activeVersion.Version)
			: null;

		return new InstalledPluginBody(plugin.PluginId,
			manifest?.Name ?? plugin.PluginId,
			manifest?.Description,
			manifest?.Publisher?.Name,
			manifest?.License,
			manifest?.Homepage,
			versions,
			plugin.ActiveVersion?.Version,
			manifest?.Permissions ?? [],
			trustRecord?.AdmittedVerdict,
			trustRecord?.CertificateId);
	}

	private static PluginInstallActionResponse Failure(string code, string message)
	{
		return new PluginInstallActionResponse(false,
			null,
			null,
			null,
			false,
			false,
			[],
			new TransportError { Code = code, Message = message });
	}

	private static PluginInstallActionResponse ToResponse(PluginInstallResult result)
	{
		var warnings = result.Warnings.Select(warning => new PluginInstallWarningBody(warning.Code,
			ToWireSeverity(warning.Severity),
			warning.Message,
			warning.SubjectId)).ToList();

		var error = result.Success
			? null
			: new TransportError
			{
				Code = ToErrorCode(result.Error ?? PluginInstallError.Failed),
				Message = result.ErrorMessage ?? string.Empty
			};

		return new PluginInstallActionResponse(result.Success,
			result.PluginId,
			result.Version,
			result.PreviousVersion,
			result.Activated,
			result.RolledBack,
			warnings,
			error,
			result.Publisher is { } publisher
				? new PluginPublisherBody(publisher.Name, publisher.Id, publisher.Url)
				: null,
			result.Signature is { } signature
				? new PluginSignatureBody(ToSignatureVerdict(signature.Verdict),
					signature.Message,
					result.SignedWith?.Algorithm,
					result.SignedWith?.KeyId,
					ToTrustCategory(signature.Verdict),
					signature.CertificateId,
					signature.IsTrusted)
				: null,
			result.Name is { } name
				? new PluginArtifactBody(name,
					result.Description,
					result.IconDataUri,
					result.SupportedOnThisPlatform ?? false)
				: null);
	}

	private static string ToWireSeverity(PluginInstallWarningSeverity severity) => severity switch
	{
		PluginInstallWarningSeverity.Blocking => "blocking",
		_ => "advisory"
	};

	// The four-value summary a client can branch on without knowing every fine-grained verdict. Only
	// Trusted is ever "valid" - every other verdict, including one this build does not yet know the name
	// of (a future enum member reaching here through a cast), lands in "invalid" or "unverified", never
	// silently in "valid".
	internal static string ToSignatureVerdict(PluginTrustVerdict verdict) => verdict switch
	{
		PluginTrustVerdict.Trusted => "valid",
		PluginTrustVerdict.Unsigned => "not_signed",
		PluginTrustVerdict.RevocationUnavailable => "unverified",
		PluginTrustVerdict.VerificationUnavailable => "unverified",
		_ => "invalid"
	};

	internal static string ToTrustCategory(PluginTrustVerdict verdict) => verdict switch
	{
		PluginTrustVerdict.Unsigned => "unsigned",
		PluginTrustVerdict.Trusted => "trusted",
		PluginTrustVerdict.Malformed => "malformed",
		PluginTrustVerdict.SignatureInvalid => "signature_invalid",
		PluginTrustVerdict.ContentMismatch => "content_mismatch",
		PluginTrustVerdict.UntrustedRoot => "untrusted_root",
		PluginTrustVerdict.WrongCertificatePurpose => "wrong_certificate_purpose",
		PluginTrustVerdict.CertificateNotValidAtSignature => "certificate_not_valid_at_signature",
		PluginTrustVerdict.Revoked => "revoked",
		PluginTrustVerdict.RevocationUnavailable => "revocation_unavailable",
		PluginTrustVerdict.VerificationUnavailable => "verification_unavailable",
		_ => "unknown"
	};

	internal static string ToErrorCode(PluginInstallError error) => error switch
	{
		PluginInstallError.ArtifactNotFound => "artifact_not_found",
		PluginInstallError.ArtifactTooLarge => "artifact_too_large",
		PluginInstallError.InvalidArchive => "invalid_archive",
		PluginInstallError.UnsafeEntry => "unsafe_entry",
		PluginInstallError.ArtifactLimitExceeded => "artifact_limit_exceeded",
		PluginInstallError.ManifestMissing => "manifest_missing",
		PluginInstallError.ManifestInvalid => "manifest_invalid",
		PluginInstallError.IdMismatch => "id_mismatch",
		PluginInstallError.Incompatible => "incompatible",
		PluginInstallError.HashMismatch => "hash_mismatch",
		PluginInstallError.SignatureInvalid => "signature_invalid",
		PluginInstallError.AlreadyInstalled => "already_installed",
		PluginInstallError.StagingFailed => "staging_failed",
		PluginInstallError.ActivationFailed => "activation_failed",
		PluginInstallError.HealthValidationFailed => "health_validation_failed",
		PluginInstallError.DependencyInUse => "dependency_in_use",
		PluginInstallError.NotInstalled => "not_installed",
		PluginInstallError.Cancelled => "cancelled",
		PluginInstallError.SignatureUntrusted => "signature_untrusted",
		PluginInstallError.SignatureRevoked => "signature_revoked",
		PluginInstallError.SignatureUnverifiable => "signature_unverifiable",
		PluginInstallError.UnsignedNotPermitted => "unsigned_not_permitted",
		PluginInstallError.TrustDowngrade => "trust_downgrade",
		_ => "failed"
	};
}
