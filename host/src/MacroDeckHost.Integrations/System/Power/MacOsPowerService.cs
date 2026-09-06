using System.Runtime.Versioning;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.System.Power;

[SupportedOSPlatform("macos")]
internal sealed class MacOsPowerService : IPowerService
{
	private static readonly ILogger _logger = IntegrationLog.For<IPowerService>(SystemIntegration.IntegrationId);

	public bool IsSupported => true;

	public bool Supports(PowerOperation operation)
		=> operation switch
		{
			PowerOperation.Lock => File.Exists(MacOsPowerCommandResolver.CgSessionPath),
			PowerOperation.Hibernate => false,
			_ => true
		};

	public async Task<PowerResult> ExecuteAsync(
		PowerOperation operation,
		bool force,
		CancellationToken cancellationToken = default)
	{
		if (!Supports(operation))
		{
			return PowerResult.Failed(UnsupportedReason(operation), ActionErrorCodes.Unavailable);
		}

		var (fileName, arguments) = MacOsPowerCommandResolver.Resolve(operation, force);
		var result = await ProcessRunner.RunWithResultAsync(fileName, arguments, cancellationToken);
		if (result.Succeeded)
		{
			return PowerResult.Succeeded();
		}

		_logger.Warning("'{FileName} {Arguments}' exited with code {ExitCode}: {Error}",
			fileName,
			string.Join(' ', arguments),
			result.ExitCode,
			result.StandardError);

		// osascript driving System Events is gated by TCC Automation consent: the first run raises a
		// consent prompt, a denial exits with AppleEvent error -1743, and an unbundled host can instead
		// fail with -600 ("application isn't running") with no prompt at all because TCC never
		// recognized it as a requester. Either way it is a permission problem, not a generic failure.
		var errorCode = fileName == "osascript" ? ActionErrorCodes.PermissionDenied : null;
		return PowerResult.Failed(FailureMessage(operation), errorCode);
	}

	private static LocalizedText UnsupportedReason(PowerOperation operation)
		=> operation == PowerOperation.Hibernate
			? AppStrings.Integrations.System.Errors.Power.MacOsNoHibernation()
			: AppStrings.Integrations.System.Errors.Power.MacOsNoLockMechanism();

	private static LocalizedText FailureMessage(PowerOperation operation)
		=> operation switch
		{
			PowerOperation.Lock => AppStrings.Integrations.System.Errors.Power.MacOsScreenLockFailed(),
			PowerOperation.Sleep => AppStrings.Integrations.System.Errors.Power.SleepFailed(),
			PowerOperation.Restart => AppStrings.Integrations.System.Errors.Power.MacOsRestartDenied(),
			PowerOperation.ShutDown => AppStrings.Integrations.System.Errors.Power.MacOsShutDownDenied(),
			_ => AppStrings.Integrations.System.Errors.Power.OperationFailed()
		};
}
