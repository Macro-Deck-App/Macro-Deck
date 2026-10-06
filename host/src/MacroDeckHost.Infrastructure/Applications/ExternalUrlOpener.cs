using MacroDeckHost.Application.Applications;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Integrations.System.Application;

namespace MacroDeckHost.Infrastructure.Applications;

public sealed class ExternalUrlOpener : IExternalUrlOpener
{
	private readonly IHostLockState _lockState;
	private readonly IApplicationService _applications;

	public ExternalUrlOpener(IHostLockState lockState)
		: this(lockState, ApplicationServiceFactory.Create())
	{
	}

	internal ExternalUrlOpener(IHostLockState lockState, IApplicationService applications)
	{
		_lockState = lockState;
		_applications = applications;
	}

	public Result<ExternalUrlOpenError> Open(string? url)
	{
		if (!ExternalUrls.TryNormalizeWebUrl(url, out var normalized))
		{
			return Result.Fail(ExternalUrlOpenError.InvalidUrl);
		}

		// A press on a deck must not open a browser on a computer whose user stepped away from it.
		if (_lockState.IsLocked)
		{
			return Result.Fail(ExternalUrlOpenError.HostLocked);
		}

		if (!_applications.IsSupported)
		{
			return Result.Fail(ExternalUrlOpenError.NotSupported);
		}

		_applications.OpenWebsite(normalized);
		return Result.Ok<ExternalUrlOpenError>();
	}
}
