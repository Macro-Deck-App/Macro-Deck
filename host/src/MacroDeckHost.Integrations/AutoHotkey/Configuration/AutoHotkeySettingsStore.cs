using System.Text.Json;
using MacroDeck.Sdk.Storage;
using Serilog;

namespace MacroDeckHost.Integrations.AutoHotkey.Configuration;

internal static class AutoHotkeySettingsStore
{
	public const string Key = "settings";

	private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

	public static async Task<AutoHotkeySettings> ReadAsync(
		IIntegrationStorage storage,
		ILogger logger,
		CancellationToken cancellationToken = default)
	{
		var result = await storage.ReadAsync(Key, IsValid, cancellationToken).ConfigureAwait(false);
		switch (result.Status)
		{
			case IntegrationStorageReadStatus.Found:
				var document = JsonSerializer.Deserialize<SettingsDocument>(result.Content!, _json)!;
				var manual = document.ExecutableMode == AutoHotkeyConfigKeys.ManualMode
					&& !string.IsNullOrWhiteSpace(document.ExecutablePath)
						? document.ExecutablePath.Trim()
						: null;
				return new AutoHotkeySettings(manual, AutoHotkeySettings.ParseGracePeriod(document.GracePeriodMs));
			case IntegrationStorageReadStatus.Unreadable:
				logger.Warning("The AutoHotkey settings could not be read; the defaults apply until they are saved");
				return AutoHotkeySettings.Default;
			default:
				return AutoHotkeySettings.Default;
		}
	}

	public static Task WriteAsync(
		IIntegrationStorage storage,
		AutoHotkeySettings settings,
		CancellationToken cancellationToken = default)
	{
		var document = new SettingsDocument
		{
			ExecutableMode = settings.ManualExecutable is null
				? AutoHotkeyConfigKeys.AutomaticMode
				: AutoHotkeyConfigKeys.ManualMode,
			ExecutablePath = settings.ManualExecutable,
			GracePeriodMs = settings.GracePeriod.TotalMilliseconds
		};
		return storage.WriteAsync(Key, JsonSerializer.Serialize(document, _json), cancellationToken);
	}

	internal static bool IsValid(string content)
	{
		try
		{
			return JsonSerializer.Deserialize<SettingsDocument>(content, _json) is
			{
				ExecutableMode: AutoHotkeyConfigKeys.AutomaticMode or AutoHotkeyConfigKeys.ManualMode
			};
		}
		catch (JsonException)
		{
			return false;
		}
	}

	private sealed record SettingsDocument
	{
		public string? ExecutableMode { get; init; }

		public string? ExecutablePath { get; init; }

		public double GracePeriodMs { get; init; }
	}
}
