using System.ComponentModel;
using System.Globalization;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Integrations.Steam.State;
using MacroDeckHost.Platform.Applications;
using Serilog;

namespace MacroDeckHost.Integrations.Steam.Native;

internal sealed class SteamLauncher(IApplicationService applications)
{
	private static readonly ILogger _logger = IntegrationLog.For<SteamLauncher>(SteamIntegration.IntegrationId);

	public static string? BuildUri(SteamCommand command)
	{
		return command.Kind switch
		{
			SteamCommandKind.LaunchSteam => "steam://open/main",
			SteamCommandKind.LaunchGame when command.AppId > 0 => $"steam://rungameid/{Format(command.AppId)}",
			SteamCommandKind.OpenStorePage when command.AppId > 0 => $"steam://store/{Format(command.AppId)}",
			SteamCommandKind.OpenLibraryPage when command.AppId > 0
				=> $"steam://nav/games/details/{Format(command.AppId)}",
			_ => null
		};
	}

	public static int? ParseAppId(string? text)
	{
		return text is { Length: > 0 and <= 10 }
			&& text.All(char.IsAsciiDigit)
			&& int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var appId)
			&& appId > 0
				? appId
				: null;
	}

	public Task<SteamCommandOutcome> ExecuteAsync(SteamCommand command, CancellationToken cancellationToken)
	{
		if (BuildUri(command) is not { } uri)
		{
			return Task.FromResult(SteamCommandOutcome.InvalidAppId);
		}

		try
		{
			applications.OpenUri(uri);
			return Task.FromResult(SteamCommandOutcome.Succeeded);
		}
		catch (Exception exception) when (exception is NotSupportedException
			or InvalidOperationException
			or Win32Exception
			or IOException)
		{
			_logger.Warning(exception, "Failed to open {Uri}", uri);
			return Task.FromResult(SteamCommandOutcome.Failed);
		}
	}

	private static string Format(int appId)
	{
		return appId.ToString(CultureInfo.InvariantCulture);
	}
}
