using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;
using Serilog;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Autostart;

public class AutostartService : IAutostartService
{
	public const string ShellExecutableEnvironmentVariable
		= MacroDeckDataRootResolver.ShellExecutableEnvironmentVariable;

	private readonly ILogger _logger = Log.ForContext<AutostartService>();
	private readonly bool _portable;
	private readonly IAutostartRegistrar _registrar;
	private readonly string? _shellExecutable;

	public AutostartService(IAutostartRegistrar registrar)
		: this(registrar,
			ResolveShellExecutable(),
			MacroDeckDataRootResolver.IsPortable(DataRootEnvironment.Current()))
	{
	}

	public AutostartService(IAutostartRegistrar registrar, string? shellExecutable)
		: this(registrar, shellExecutable, false)
	{
	}

	public AutostartService(IAutostartRegistrar registrar, string? shellExecutable, bool portable)
	{
		_registrar = registrar;
		_shellExecutable = shellExecutable;
		_portable = portable;
	}

	private bool Supported => _registrar.IsSupported && _shellExecutable is not null;

	public AutostartSettings GetSettings()
	{
		if (!Supported)
		{
			return new AutostartSettings(false, false, false);
		}

		var registration = ReadOwnRegistration();
		return new AutostartSettings(true, registration is not null, registration?.OpenMinimized ?? false);
	}

	public Result<AutostartSettings, AutostartError> Update(bool enabled, bool openMinimized)
	{
		if (!Supported)
		{
			return Result.Fail<AutostartSettings, AutostartError>(AutostartError.NotSupported,
				"Autostart is only available in the installed desktop app");
		}

		try
		{
			if (enabled)
			{
				_registrar.Write(new AutostartRegistration(_shellExecutable!, openMinimized));
			}
			else if (!_portable || ReadOwnRegistration() is not null)
			{
				_registrar.Remove();
			}
		}
		catch (Exception e)
		{
			_logger.Error(e, "Failed to update the autostart login item");
			return Result.Fail<AutostartSettings, AutostartError>(AutostartError.RegistrationFailed,
				"The login item could not be updated");
		}

		return Result.Ok<AutostartSettings, AutostartError>(GetSettings());
	}

	public void RefreshRegistration()
	{
		// The login item is one per user account, so a portable copy taking it over on start would
		// leave an installed copy starting from a removable drive.
		if (!Supported || _portable)
		{
			return;
		}

		var registration = TryRead();
		if (registration is null || registration.ExecutablePath == _shellExecutable)
		{
			return;
		}

		_logger.Information("Autostart entry points at {Stale}, re-registering for {Current}",
			registration.ExecutablePath,
			_shellExecutable);
		Update(true, registration.OpenMinimized);
	}

	private AutostartRegistration? ReadOwnRegistration()
	{
		var registration = TryRead();
		return _portable && registration?.ExecutablePath != _shellExecutable ? null : registration;
	}

	private AutostartRegistration? TryRead()
	{
		try
		{
			return _registrar.Read();
		}
		catch (Exception e)
		{
			_logger.Warning(e, "Failed to read the autostart login item");
			return null;
		}
	}

	internal static string? ResolveShellExecutable()
	{
		var path = Environment.GetEnvironmentVariable(ShellExecutableEnvironmentVariable);
		return string.IsNullOrWhiteSpace(path) || !File.Exists(path) ? null : path;
	}
}
