using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Store;
using MacroDeckHost.Application.Store.Installation;
using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Store.Operations;
using MacroDeckHost.Application.Store.Updates;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Store;
using Mediator;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/store")]
public class StoreController : ControllerBase
{
	private readonly IStoreCatalogQueryService _catalogQuery;
	private readonly IStoreRegistryRefresher _refresher;
	private readonly IStoreInstallCoordinator _installCoordinator;
	private readonly IStoreOperationTracker _operationTracker;
	private readonly IStoreUpdateDetector _updateDetector;
	private readonly IStoreUpdateState _updateState;
	private readonly IStoreArtifactDownloader _downloader;
	private readonly IAppPreferenceService _preferences;
	private readonly IMacroDeckPaths _paths;
	private readonly StoreRegistryOptions _options;
	private readonly IMediator _mediator;
	private readonly IStoreUninstallService _uninstallService;

	public StoreController(IStoreCatalogQueryService catalogQuery,
		IStoreRegistryRefresher refresher,
		IStoreInstallCoordinator installCoordinator,
		IStoreOperationTracker operationTracker,
		IStoreUpdateDetector updateDetector,
		IStoreUpdateState updateState,
		IStoreArtifactDownloader downloader,
		IAppPreferenceService preferences,
		IMacroDeckPaths paths,
		StoreRegistryOptions options,
		IMediator mediator,
		IStoreUninstallService uninstallService)
	{
		_catalogQuery = catalogQuery;
		_refresher = refresher;
		_installCoordinator = installCoordinator;
		_operationTracker = operationTracker;
		_updateDetector = updateDetector;
		_updateState = updateState;
		_downloader = downloader;
		_preferences = preferences;
		_paths = paths;
		_options = options;
		_mediator = mediator;
		_uninstallService = uninstallService;
	}

	[HttpGet("status")]
	public async Task<GetStoreStatusResponse> GetStatus() =>
		new()
		{
			Registry = StoreRegistryStatusBodyFactory.Create(_refresher.Status),
			DeveloperMode = (await _preferences.GetDeveloper()).Enabled
		};

	[HttpPost("registry/refresh")]
	public async Task<RefreshStoreRegistryResponse> RefreshRegistry(CancellationToken ct)
	{
		var result = await _refresher.Refresh(ct);
		if (result.Success)
		{
			await _mediator.Publish(new StoreRegistryRefreshedNotification(_refresher.Status), ct);
		}

		return new RefreshStoreRegistryResponse
		{
			Success = result.Success,
			Registry = StoreRegistryStatusBodyFactory.Create(_refresher.Status),
			Error = result.Success
				? null
				: new TransportError
				{
					Code = ToErrorCode(result.Error ?? RegistryRefreshError.NetworkFailure),
					Message = result.ErrorMessage ?? "The registry could not be refreshed."
				}
		};
	}

	[HttpGet("catalog")]
	public GetStoreCatalogResponse GetCatalog([FromQuery] StoreExtensionKind? kind,
		[FromQuery] string? search,
		[FromQuery] StoreCatalogSection? section,
		[FromQuery] int skip = 0,
		[FromQuery] int take = StoreCatalogQuery.MaxTake,
		[FromQuery] StoreExtensionKind[]? kinds = null)
	{
		var query = new StoreCatalogQuery
		{
			Kinds = kinds is { Length: > 0 } ? kinds : kind.HasValue ? [kind.Value] : null,
			Search = search,
			Section = section ?? StoreCatalogSection.All,
			Skip = skip,
			Take = take
		};

		var result = _catalogQuery.Query(query);
		var page = result.Success ? result.Data! : StoreCatalogPage.Empty;

		return new GetStoreCatalogResponse
		{
			Items = page.Items.Select(ToItemBody).ToList(),
			Total = page.Total,
			Registry = StoreRegistryStatusBodyFactory.Create(_refresher.Status)
		};
	}

	[HttpGet("catalog/{kind}/{id}")]
	public GetStoreExtensionResponse GetExtension(StoreExtensionKind kind, string id)
	{
		var result = _catalogQuery.Find(kind, id);
		if (!result.Success)
		{
			return new GetStoreExtensionResponse
			{
				Error = new TransportError
				{
					Code = ToErrorCode(result.Error ?? StoreCatalogError.NotFound),
					Message = result.ErrorMessage ?? "The extension could not be found."
				}
			};
		}

		return new GetStoreExtensionResponse { Extension = ToDetailBody(result.Data!) };
	}

	[HttpGet("updates")]
	public GetStoreUpdatesResponse GetUpdates() =>
		new() { Updates = _updateState.Current.Select(StoreAvailableUpdateBodyFactory.Create).ToList() };

	[HttpPost("updates/check")]
	public async Task<GetStoreUpdatesResponse> CheckUpdates(CancellationToken ct)
	{
		var updates = _updateDetector.Check();
		await _mediator.Publish(new StoreUpdatesChangedNotification(updates), ct);
		return new GetStoreUpdatesResponse
			{ Updates = updates.Select(StoreAvailableUpdateBodyFactory.Create).ToList() };
	}

	[HttpGet("operations")]
	public GetStoreOperationsResponse GetOperations() =>
		new() { Operations = _operationTracker.Snapshot().Select(StoreOperationBodyFactory.Create).ToList() };

	[HttpPost("install")]
	public StoreOperationActionResponse Install(InstallStoreExtensionRequest body)
	{
		if (string.IsNullOrWhiteSpace(body.PackageId))
		{
			return Failure("invalid_package_id", "A package id is required.");
		}

		var operation = _installCoordinator.Install(body.Kind, body.PackageId, body.Version, body.AllowUnsigned);
		return new StoreOperationActionResponse
			{ Success = true, Operation = StoreOperationBodyFactory.Create(operation) };
	}

	[HttpPost("uninstall")]
	public async Task<UninstallStoreExtensionResponse> Uninstall(UninstallStoreExtensionRequest body,
		CancellationToken ct)
	{
		var result = await _uninstallService.Uninstall(body.Kind, body.Id, ct);
		if (result.Success)
		{
			return new UninstallStoreExtensionResponse { Success = true };
		}

		var error = result.Error ?? StoreUninstallError.Failed;
		return new UninstallStoreExtensionResponse
		{
			Success = false,
			Error = new TransportError
				{ Code = error.ToString(), Message = result.ErrorMessage ?? "The extension could not be uninstalled." }
		};
	}

	[HttpPost("operations/{id}/retry")]
	public StoreOperationActionResponse Retry(Guid id)
	{
		var operation = _installCoordinator.Retry(id);
		return operation is null
			? Failure("not_found", "The operation could not be retried.")
			: new StoreOperationActionResponse
				{ Success = true, Operation = StoreOperationBodyFactory.Create(operation) };
	}

	[HttpPost("operations/{id}/cancel")]
	public StoreOperationActionResponse Cancel(Guid id)
	{
		if (!_installCoordinator.Cancel(id))
		{
			return Failure("not_found", "The operation could not be cancelled.");
		}

		var operation = _operationTracker.Find(id);
		return new StoreOperationActionResponse
		{
			Success = true,
			Operation = operation is null ? null : StoreOperationBodyFactory.Create(operation)
		};
	}

	[HttpDelete("operations/{id}")]
	public StoreOperationActionResponse Dismiss(Guid id) =>
		_installCoordinator.Dismiss(id)
			? new StoreOperationActionResponse { Success = true }
			: Failure("not_found", "The operation could not be dismissed.");

	[HttpGet("media/{kind}/{id}/icon")]
	public Task<IActionResult> GetIcon(StoreExtensionKind kind, string id, CancellationToken ct) =>
		ServeMedia(kind, id, screenshotIndex: null, ct);

	[HttpGet("media/{kind}/{id}/screenshots/{index}")]
	public Task<IActionResult> GetScreenshot(StoreExtensionKind kind, string id, int index, CancellationToken ct) =>
		ServeMedia(kind, id, index, ct);

	// Only an asset the verified catalog entry itself declares (icon, or a screenshot at a declared
	// index) is ever served, fetched through the artifact downloader (https-only, digest-checked) and
	// never from a client-supplied URL. The content type is decided by sniffing the downloaded bytes
	// against a fixed set of image signatures, never by trusting a declared or client-supplied string,
	// so the response header cannot be steered by registry data.
	private async Task<IActionResult> ServeMedia(StoreExtensionKind kind,
		string id,
		int? screenshotIndex,
		CancellationToken ct)
	{
		var found = _catalogQuery.Find(kind, id);
		if (!found.Success)
		{
			return NotFound();
		}

		var release = found.Data!.Entry.LatestRelease;
		var asset = screenshotIndex is null
			? release.Icon
			: screenshotIndex >= 0 && screenshotIndex < release.Screenshots.Count
				? release.Screenshots[screenshotIndex.Value]
				: null;

		if (asset is null)
		{
			return NotFound();
		}

		var digest = asset.Sha256.ToLowerInvariant();
		var cachePath = Path.Combine(_paths.StoreMediaDirectory, digest);

		byte[] bytes;
		if (System.IO.File.Exists(cachePath))
		{
			bytes = await System.IO.File.ReadAllBytesAsync(cachePath, ct);
		}
		else
		{
			if (asset.Size > _options.MaxMediaBytes)
			{
				return StatusCode(StatusCodes.Status502BadGateway);
			}

			var operationId = Guid.CreateVersion7();
			try
			{
				var download = await _downloader.Download(asset.Url,
					digest,
					asset.Size,
					operationId,
					progress: null,
					ct);
				if (!download.Success)
				{
					return StatusCode(StatusCodes.Status502BadGateway);
				}

				Directory.CreateDirectory(_paths.StoreMediaDirectory);
				System.IO.File.Copy(download.FilePath!, cachePath, overwrite: true);
				bytes = await System.IO.File.ReadAllBytesAsync(cachePath, ct);
			}
			finally
			{
				DeleteStagingDirectory(operationId);
			}
		}

		var contentType = SniffImageContentType(bytes);
		if (contentType is null)
		{
			return StatusCode(StatusCodes.Status502BadGateway);
		}

		Response.Headers.Append("X-Content-Type-Options", "nosniff");
		Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
		Response.Headers.CacheControl = "public, max-age=604800, immutable";
		return File(bytes, contentType);
	}

	private void DeleteStagingDirectory(Guid operationId)
	{
		var directory = Path.Combine(_paths.StoreStagingDirectory, operationId.ToString("N"));
		try
		{
			if (Directory.Exists(directory))
			{
				Directory.Delete(directory, recursive: true);
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
		}
	}

	private static string? SniffImageContentType(byte[] bytes)
	{
		if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
		{
			return "image/png";
		}

		if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
		{
			return "image/jpeg";
		}

		if (bytes.Length >= 6 && bytes[0] == 'G' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == '8')
		{
			return "image/gif";
		}

		if (bytes.Length >= 12 &&
			bytes[0] == 'R' &&
			bytes[1] == 'I' &&
			bytes[2] == 'F' &&
			bytes[3] == 'F' &&
			bytes[8] == 'W' &&
			bytes[9] == 'E' &&
			bytes[10] == 'B' &&
			bytes[11] == 'P')
		{
			return "image/webp";
		}

		return IsSvg(bytes) ? "image/svg+xml" : null;
	}

	// Registry icons are commonly SVG. An SVG is script-capable, so it is served only with a policy that
	// forbids the document loading or running anything, on top of the nosniff header; the UI renders it
	// through <img>, where script never executes anyway.
	private static bool IsSvg(byte[] bytes)
	{
		var head = System.Text.Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 1024)).TrimStart('\uFEFF');
		var index = 0;
		while (index < head.Length && char.IsWhiteSpace(head[index]))
		{
			index++;
		}

		head = head[index..];
		return head.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) ||
			(head.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) &&
				head.Contains("<svg", StringComparison.OrdinalIgnoreCase));
	}

	private StoreCatalogItemBody ToItemBody(StoreCatalogItem item) => new()
	{
		Kind = item.Entry.Kind,
		Id = item.Entry.Id,
		Name = item.Entry.Name,
		Description = item.Entry.Description,
		Publisher = item.Entry.Publisher,
		LatestVersion = item.Entry.LatestVersion,
		CreatedAt = item.Entry.CreatedAt,
		UpdatedAt = item.Entry.UpdatedAt,
		InstallState = item.InstallState,
		InstalledVersion = item.InstalledVersion,
		UnsupportedReason = item.UnsupportedReason,
		Trust = item.Trust,
		HasIcon = item.Entry.LatestRelease.Icon is not null,
		ActiveOperationId = _operationTracker.FindLive(item.Entry.Kind, item.Entry.Id)?.Id
	};

	private StoreExtensionDetailBody ToDetailBody(StoreCatalogItem item) => new()
	{
		Kind = item.Entry.Kind,
		Id = item.Entry.Id,
		Name = item.Entry.Name,
		Description = item.Entry.Description,
		Publisher = item.Entry.Publisher,
		LatestVersion = item.Entry.LatestVersion,
		CreatedAt = item.Entry.CreatedAt,
		UpdatedAt = item.Entry.UpdatedAt,
		InstallState = item.InstallState,
		InstalledVersion = item.InstalledVersion,
		UnsupportedReason = item.UnsupportedReason,
		Trust = item.Trust,
		HasIcon = item.Entry.LatestRelease.Icon is not null,
		ActiveOperationId = _operationTracker.FindLive(item.Entry.Kind, item.Entry.Id)?.Id,
		LongDescription = item.Entry.LongDescription,
		Changelog = item.Entry.Changelog,
		Repository = item.Entry.Repository,
		License = item.Entry.License,
		DownloadSize = item.Entry.LatestRelease.Size,
		SupportedOperatingSystems = SupportedOperatingSystems(item.Entry.SupportedRids),
		Languages = [.. item.Entry.Languages],
		Screenshots = item.Entry.LatestRelease.Screenshots
			.Select((asset, index) => new StoreScreenshotBody { Index = index, Caption = asset.Caption })
			.ToList(),
		History = item.Entry.History
			.Select(entry => new StoreVersionHistoryBody
			{
				Version = entry.Version,
				ReleasedAt = entry.ReleasedAt,
				Changelog = entry.Changelog
			})
			.ToList()
	};

	// A package without a runtime identifier list is portable, which is the common case for icon packs
	// and templates. Architectures are collapsed to their operating system: the store answers "does this
	// run on my machine", and the installer is what enforces the exact runtime identifier.
	private static List<string> SupportedOperatingSystems(IReadOnlyList<string> supportedRids) =>
		supportedRids
			.Select(rid => rid.Split('-')[0] switch
			{
				"win" => "Windows",
				"osx" => "macOS",
				"linux" => "Linux",
				_ => null
			})
			.Where(name => name is not null)
			.Select(name => name!)
			.Distinct(StringComparer.Ordinal)
			.Order(StringComparer.Ordinal)
			.ToList();

	private static StoreOperationActionResponse Failure(string code, string message) =>
		new() { Success = false, Error = new TransportError { Code = code, Message = message } };

	private static string ToErrorCode(StoreCatalogError error) => error switch
	{
		StoreCatalogError.RegistryUnavailable => "registry_unavailable",
		StoreCatalogError.StoreDisabled => "store_disabled",
		StoreCatalogError.NotFound => "not_found",
		_ => "failed"
	};

	private static string ToErrorCode(RegistryRefreshError error) => error switch
	{
		RegistryRefreshError.Disabled => "disabled",
		RegistryRefreshError.NetworkFailure => "network_failure",
		RegistryRefreshError.Malformed => "malformed",
		RegistryRefreshError.BudgetExceeded => "budget_exceeded",
		RegistryRefreshError.SizeMismatch => "size_mismatch",
		RegistryRefreshError.SequenceRollback => "sequence_rollback",
		RegistryRefreshError.SignatureInvalid => "signature_invalid",
		RegistryRefreshError.CertificateUntrusted => "certificate_untrusted",
		RegistryRefreshError.SigningKeyRevoked => "signing_key_revoked",
		RegistryRefreshError.SnapshotUnchanged => "snapshot_unchanged",
		RegistryRefreshError.StorageFailure => "storage_failure",
		_ => "failed"
	};
}
