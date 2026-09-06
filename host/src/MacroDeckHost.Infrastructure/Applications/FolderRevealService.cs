using MacroDeckHost.Application.Applications;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Integrations.System.Application;
using Serilog;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Applications;

public class FolderRevealService : IFolderRevealService
{
	private readonly ILogger _logger = Log.ForContext<FolderRevealService>();
	private readonly IApplicationService _applications;

	public FolderRevealService()
		: this(ApplicationServiceFactory.Create())
	{
	}

	// Seam for tests: inject a fake IApplicationService instead of the real platform one.
	internal FolderRevealService(IApplicationService applications)
	{
		_applications = applications;
	}

	public Result<FolderRevealError> Reveal(string path)
	{
		if (!_applications.IsSupported)
		{
			return Result.Fail<FolderRevealError>(FolderRevealError.NotSupported,
				"No supported application integration is available on this platform.");
		}

		try
		{
			Directory.CreateDirectory(path);

			// Unlike OpenFile/OpenWebsite, OpenFolder does not catch its own exceptions, so
			// reaching this point only proves the platform command was spawned, not that it succeeded.
			_applications.OpenFolder(path);
		}
		catch (Exception exception)
		{
			_logger.Warning(exception, "Failed to open data directory '{Path}'", path);
			return Result.Fail<FolderRevealError>(FolderRevealError.OpenFailed, exception.Message);
		}

		return Result.Ok<FolderRevealError>();
	}
}
