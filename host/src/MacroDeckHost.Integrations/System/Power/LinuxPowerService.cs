using System.Runtime.Versioning;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.System.Power;

[SupportedOSPlatform("linux")]
internal sealed class LinuxPowerService : IPowerService
{
	private static readonly ILogger _logger = IntegrationLog.For<IPowerService>(SystemIntegration.IntegrationId);

	private readonly bool _hasSystemctl = ProcessRunner.CommandExists("systemctl");
	private readonly bool _hasLoginctl = ProcessRunner.CommandExists("loginctl");
	private readonly bool _hasXdgScreensaver = ProcessRunner.CommandExists("xdg-screensaver");

	public bool IsSupported => _hasSystemctl || _hasLoginctl || _hasXdgScreensaver;

	public bool Supports(PowerOperation operation)
		=> operation == PowerOperation.Lock
			? _hasLoginctl || _hasXdgScreensaver
			: _hasSystemctl || _hasLoginctl;

	public async Task<PowerResult> ExecuteAsync(
		PowerOperation operation,
		bool force,
		CancellationToken cancellationToken = default)
	{
		if (!Supports(operation))
		{
			return PowerResult.Failed(UnsupportedReason(operation), ActionErrorCodes.Unavailable);
		}

		var (fileName, arguments) = operation == PowerOperation.Lock
			? LinuxPowerCommandResolver.ResolveLock(_hasLoginctl)
			: LinuxPowerCommandResolver.Resolve(operation, force, _hasSystemctl);

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

		return PowerResult.Failed(FailureMessage(operation));
	}

	private static LocalizedText UnsupportedReason(PowerOperation operation)
		=> operation == PowerOperation.Lock
			? AppStrings.Integrations.System.Errors.Power.LinuxNoLockCommand()
			: AppStrings.Integrations.System.Errors.Power.LinuxNoControlCommand();

	private static LocalizedText FailureMessage(PowerOperation operation)
		=> operation switch
		{
			PowerOperation.Lock => AppStrings.Integrations.System.Errors.Power.LockFailed(),
			PowerOperation.Sleep => AppStrings.Integrations.System.Errors.Power.SleepFailed(),
			PowerOperation.Hibernate => AppStrings.Integrations.System.Errors.Power.HibernateFailed(),
			PowerOperation.Restart => AppStrings.Integrations.System.Errors.Power.LinuxRestartRefused(),
			PowerOperation.ShutDown => AppStrings.Integrations.System.Errors.Power.LinuxShutDownRefused(),
			_ => AppStrings.Integrations.System.Errors.Power.OperationFailed()
		};
}
