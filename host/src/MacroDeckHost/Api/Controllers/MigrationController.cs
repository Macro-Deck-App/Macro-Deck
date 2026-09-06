using MacroDeckHost.Api.Support;
using MacroDeckHost.Application.Migration;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Migration;
using MacroDeckHost.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

/// <summary>
/// Reads another application's data directory and turns it into Macro Deck profiles.
/// </summary>
/// <remarks>
/// The endpoints that take a host filesystem path are restricted to the trusted loopback path for the
/// same reason the path-based archive import is (ADR 0009): a remote client must not gain an oracle for
/// what exists on the machine running the host. The upload endpoints carry no path and are therefore not
/// restricted, exactly as the multipart profile import is not - they are what the browser-served
/// configuration UI uses, where there is no native file picker and no host path to name. They also carry
/// no size limit: a Macro Deck 2 backup bundles every installed plugin's binaries, so how big one is
/// says nothing about how much of it a migration reads, and the body streams straight to a temporary
/// file rather than into memory.
/// </remarks>
[ApiController]
[Route("api/migration")]
public class MigrationController : ControllerBase
{
	private const string MacroDeck2SourceId = "macro-deck-2";

	private readonly IMigrationService _migration;

	public MigrationController(IMigrationService migration)
	{
		_migration = migration;
	}

	[HttpGet("sources")]
	public ActionResult<MigrationSourcesResponse> GetSources()
	{
		var refusal = PortabilityHttp.RefuseUnlessDesktop(HttpContext);
		if (refusal is not null)
		{
			return BadRequest(refusal);
		}

		return new MigrationSourcesResponse
		{
			Sources = _migration.GetSources()
				.Select(source => new MigrationSourceDto
				{
					Id = source.Id,
					Name = source.Name,
					DefaultPath = source.DefaultPath
				})
				.ToList()
		};
	}

	[HttpPost("preview")]
	public async Task<MigrationPreviewResponse> Preview(MigrationRequestBody body, CancellationToken ct)
	{
		var refusal = PortabilityHttp.RefuseUnlessDesktop(HttpContext);
		return refusal is not null
			? new MigrationPreviewResponse { Success = false, Error = refusal }
			: await RunPreview(body, ct);
	}

	private async Task<MigrationPreviewResponse> RunPreview(MigrationRequestBody body, CancellationToken ct)
	{
		var result = await _migration.Preview(ToRequest(body), ct);
		return result.Success
			? new MigrationPreviewResponse { Success = true, Summary = MigrationSummary.From(result.Data!) }
			: new MigrationPreviewResponse
			{
				Success = false,
				Error = MigrationHttp.ToTransportError(result.Error!.Value, result.ErrorMessage)
			};
	}

	[HttpPost("import")]
	public async Task<MigrationImportResponse> Import(MigrationRequestBody body, CancellationToken ct)
	{
		var refusal = PortabilityHttp.RefuseUnlessDesktop(HttpContext);
		return refusal is not null
			? new MigrationImportResponse { Success = false, Error = refusal }
			: await RunImport(body, ct);
	}

	private async Task<MigrationImportResponse> RunImport(MigrationRequestBody body, CancellationToken ct)
	{
		var result = await _migration.Apply(ToRequest(body), ct);
		if (!result.Success)
		{
			return new MigrationImportResponse
			{
				Success = false,
				Error = MigrationHttp.ToTransportError(result.Error!.Value, result.ErrorMessage)
			};
		}

		var outcome = result.Data!;
		return new MigrationImportResponse
		{
			Success = true,
			Summary = MigrationSummary.From(outcome.Plan),
			ProfileIds = [.. outcome.CreatedProfileIds]
		};
	}

	/// <summary>
	/// Previews an uploaded backup archive. Unlike the path endpoints this is not restricted to the
	/// desktop shell: bytes the caller already holds are not an oracle for what exists on this machine,
	/// which is the only thing that restriction protects. It is the route the browser-served
	/// configuration UI takes, where there is no native file picker and no host path to name.
	/// </summary>
	[HttpPost("preview-upload")]
	[DisableRequestSizeLimit]
	[RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
	public async Task<MigrationPreviewResponse> PreviewUpload(
		[FromForm] IFormFile? file,
		[FromForm] string? sourceId,
		[FromForm] string? decryptionKey,
		[FromForm] bool skipDecryption,
		CancellationToken ct)
	{
		using var upload = await StagedUpload.Create(file, ct);
		if (upload.Error is not null)
		{
			return new MigrationPreviewResponse { Success = false, Error = upload.Error };
		}

		return await RunPreview(Body(sourceId, upload.Path!, decryptionKey, skipDecryption), ct);
	}

	[HttpPost("import-upload")]
	[DisableRequestSizeLimit]
	[RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
	public async Task<MigrationImportResponse> ImportUpload(
		[FromForm] IFormFile? file,
		[FromForm] string? sourceId,
		[FromForm] string? decryptionKey,
		[FromForm] bool skipDecryption,
		CancellationToken ct)
	{
		using var upload = await StagedUpload.Create(file, ct);
		if (upload.Error is not null)
		{
			return new MigrationImportResponse { Success = false, Error = upload.Error };
		}

		return await RunImport(Body(sourceId, upload.Path!, decryptionKey, skipDecryption), ct);
	}

	private static MigrationRequestBody Body(string? sourceId, string path, string? decryptionKey, bool skip)
		=> new()
		{
			SourceId = sourceId ?? MacroDeck2SourceId,
			Path = path,
			DecryptionKey = decryptionKey,
			SkipDecryption = skip
		};

	private static MigrationRequest ToRequest(MigrationRequestBody body)
		=> new(body.SourceId ?? string.Empty,
			body.Path ?? string.Empty,
			body.DecryptionKey,
			body.SkipDecryption);
}

/// <summary>
/// An uploaded backup written to a temporary file, because a migration source reads a directory tree and
/// cannot be handed a stream. Deleted when the request ends, whether or not it succeeded.
/// </summary>
internal sealed class StagedUpload : IDisposable
{
	private StagedUpload(string? path, TransportError? error)
	{
		Path = path;
		Error = error;
	}

	public string? Path { get; }

	public TransportError? Error { get; }

	public static async Task<StagedUpload> Create(IFormFile? file, CancellationToken cancellationToken)
	{
		if (file is null || file.Length == 0)
		{
			return new StagedUpload(null,
				MigrationHttp.ToTransportError(MigrationError.SourceNotFound, "A backup file is required"));
		}

		var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
			"macro-deck-migration-upload",
			Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);

		// The name carries the extension the source dispatches on, not anything the caller chose: an
		// uploaded name is untrusted and is never used to build a path.
		var path = System.IO.Path.Combine(directory, "backup.zip");
		await using (var destination = File.Create(path))
		{
			await file.CopyToAsync(destination, cancellationToken);
		}

		return new StagedUpload(path, null);
	}

	public void Dispose()
	{
		if (Path is null)
		{
			return;
		}

		try
		{
			Directory.Delete(System.IO.Path.GetDirectoryName(Path)!, recursive: true);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
		}
	}
}

internal static class MigrationHttp
{
	public static TransportError ToTransportError(MigrationError error, string? message)
		=> new() { Code = error.ToString(), Message = message ?? DefaultMessage(error) };

	private static string DefaultMessage(MigrationError error)
		=> error switch
		{
			MigrationError.UnknownSource => "That migration source is not available",
			MigrationError.SourceNotFound => "The folder does not contain a migratable installation",
			MigrationError.SourceUnreadable => "The folder could not be read",
			MigrationError.NothingToMigrate => "There is nothing to migrate in that folder",
			MigrationError.DecryptionKeyRequired => "A decryption key is needed to read the stored credentials",
			MigrationError.InvalidDecryptionKey => "That key does not decrypt the stored credentials",
			MigrationError.KeyRingLocked => "The key ring is locked, so credentials cannot be stored",
			MigrationError.DesktopOnly => "Migrating is only allowed from the desktop app.",
			_ => "The migration failed"
		};
}
