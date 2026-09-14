using System.Text.Json;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.ScreenSavers;

public readonly record struct DeviceScreenSaverSettings(
	bool Enabled,
	int IdleSeconds,
	string? ScreenSaverId,
	string? Configuration)
{
	public const int DefaultIdleSeconds = 300;

	public const int MinimumIdleSeconds = 10;

	public const int MaximumIdleSeconds = 24 * 60 * 60;

	public const int MaximumConfigurationLength = 64 * 1024;

	// A stored selection is never re-validated when read and keeping it while changing another setting is
	// not a new pick, so an unresolvable id is only refused when it is actually being chosen.
	public static Result<DeviceScreenSaverSettings, DeviceError> Validate(
		IScreenSaverRegistry registry,
		bool enabled,
		int idleSeconds,
		string? screenSaverId,
		string? configuration,
		string? storedScreenSaverId = null)
	{
		ArgumentNullException.ThrowIfNull(registry);

		if (!enabled)
		{
			idleSeconds = Math.Clamp(idleSeconds, MinimumIdleSeconds, MaximumIdleSeconds);
		}

		if (idleSeconds is < MinimumIdleSeconds or > MaximumIdleSeconds)
		{
			return Result.Fail<DeviceScreenSaverSettings, DeviceError>(DeviceError.ValidationError,
				$"The idle timeout must be between {MinimumIdleSeconds} and {MaximumIdleSeconds} seconds.");
		}

		var selected = string.IsNullOrEmpty(screenSaverId) ? null : screenSaverId;

		if (selected is not null &&
			!string.Equals(selected, storedScreenSaverId, StringComparison.Ordinal) &&
			!registry.TryResolve(selected, out _))
		{
			return Result.Fail<DeviceScreenSaverSettings, DeviceError>(DeviceError.ValidationError,
				$"No screensaver '{selected}' is available.");
		}

		if (configuration is { Length: > MaximumConfigurationLength })
		{
			return Result.Fail<DeviceScreenSaverSettings, DeviceError>(DeviceError.ValidationError,
				$"A screensaver configuration must not exceed {MaximumConfigurationLength} characters.");
		}

		if (configuration is not null && !IsJsonObject(configuration))
		{
			return Result.Fail<DeviceScreenSaverSettings, DeviceError>(DeviceError.ValidationError,
				"A screensaver configuration must be a JSON object.");
		}

		return Result.Ok<DeviceScreenSaverSettings, DeviceError>(
			new DeviceScreenSaverSettings(enabled, idleSeconds, selected, configuration));
	}

	private static bool IsJsonObject(string candidate)
	{
		if (string.IsNullOrWhiteSpace(candidate))
		{
			return false;
		}

		try
		{
			using var document = JsonDocument.Parse(candidate);
			return document.RootElement.ValueKind == JsonValueKind.Object;
		}
		catch (JsonException)
		{
			return false;
		}
	}
}
