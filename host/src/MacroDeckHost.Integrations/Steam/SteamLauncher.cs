using System.ComponentModel;
using System.Globalization;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Application.Steam;
using MacroDeckHost.Integrations.System.Application;
using Serilog;

namespace MacroDeckHost.Integrations.Steam;

internal sealed class SteamLauncher(IApplicationService applications)
{
	private static readonly ILogger _logger = IntegrationLog.For<SteamLauncher>(SteamIntegration.IntegrationId);

	public static string? BuildUri(SteamCommand command)
		=> command.Kind switch
		{
			SteamCommandKind.LaunchSteam => "steam://open/main",
			SteamCommandKind.LaunchGame when command.AppId > 0 => $"steam://rungameid/{Format(command.AppId)}",
			SteamCommandKind.OpenStorePage when command.AppId > 0 => $"steam://store/{Format(command.AppId)}",
			SteamCommandKind.OpenLibraryPage when command.AppId > 0
				=> $"steam://nav/games/details/{Format(command.AppId)}",
			_ => null
		};

	public static int? ParseAppId(string? text)
		=> text is { Length: > 0 and <= 10 } &&
			text.All(char.IsAsciiDigit) &&
			int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var appId) &&
			appId > 0
				? appId
				: null;

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
		catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException
			or Win32Exception or IOException)
		{
			_logger.Warning(exception, "Failed to open {Uri}", uri);
			return Task.FromResult(SteamCommandOutcome.Failed);
		}
	}

	private static string Format(int appId) => appId.ToString(CultureInfo.InvariantCulture);
}
