using System.Text;
using MacroDeckHost.Integrations.Adb.Models;
using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Android;

namespace MacroDeckHost.Integrations.Adb.Actions;

internal static class AdbActions
{
	private static readonly LocalizedText _deviceDescription = AppStrings.Integrations.Adb.Params.DeviceDescription();

	public static IReadOnlyList<IActionDefinition> Create(AdbActionServices services)
	{
		return
		[
			new AdbActionDefinition(services,
				"wake",
				AppStrings.Integrations.Adb.Actions.WakeName(),
				AppStrings.Integrations.Adb.Actions.WakeDescription(),
				_deviceDescription,
				[],
				(target, parameters, ct) => target.RunAsync(device => device.SendKeyAsync(AndroidKey.Wakeup, ct))),

			new AdbActionDefinition(services,
				"sleep",
				AppStrings.Integrations.Adb.Actions.SleepName(),
				AppStrings.Integrations.Adb.Actions.SleepDescription(),
				_deviceDescription,
				[],
				(target, parameters, ct) => target.RunAsync(device => device.SendKeyAsync(AndroidKey.Sleep, ct))),

			new AdbActionDefinition(services,
				"start-app",
				AppStrings.Integrations.Adb.Actions.StartAppName(),
				AppStrings.Integrations.Adb.Actions.StartAppDescription(),
				_deviceDescription,
				[
					ActionParameter.Text("package",
						AppStrings.Integrations.Adb.Params.PackageLabel(),
						placeholder: "com.example.app",
						required: true)
				],
				(target, parameters, ct) => RequireText(parameters,
					"package",
					"A package name is required.",
					package => target.RunAsync(device => device.StartAppAsync(package, ct))),
				ActionRiskCategory.Programs),

			new AdbActionDefinition(services,
				"stop-app",
				AppStrings.Integrations.Adb.Actions.StopAppName(),
				AppStrings.Integrations.Adb.Actions.StopAppDescription(),
				_deviceDescription,
				[
					ActionParameter.Text("package",
						AppStrings.Integrations.Adb.Params.PackageLabel(),
						placeholder: "com.example.app",
						required: true)
				],
				(target, parameters, ct) => RequireText(parameters,
					"package",
					"A package name is required.",
					package => target.RunAsync(device => device.ForceStopAppAsync(package, ct)))),

			new AdbActionDefinition(services,
				"open-uri",
				AppStrings.Integrations.Adb.Actions.OpenLinkName(),
				AppStrings.Integrations.Adb.Actions.OpenLinkDescription(),
				_deviceDescription,
				[
					ActionParameter.Text("uri",
						AppStrings.Integrations.Adb.Params.UriLabel(),
						placeholder: "https://example.com",
						required: true)
				],
				(target, parameters, ct) => RequireText(parameters,
					"uri",
					"A URI is required.",
					uri => target.RunAsync(device => device.OpenUriAsync(uri, ct)))),

			new AdbActionDefinition(services,
				"input-text",
				AppStrings.Integrations.Adb.Actions.TypeTextName(),
				AppStrings.Integrations.Adb.Actions.TypeTextDescription(),
				_deviceDescription,
				[
					ActionParameter.Text("text",
						AppStrings.Integrations.Adb.Params.TextLabel(),
						required: true)
				],
				(target, parameters, ct) => RequireText(parameters,
					"text",
					"Text is required.",
					text => target.RunAsync(device => device.InputTextAsync(text, ct))),
				ActionRiskCategory.Input),

			new AdbActionDefinition(services,
				"key-event",
				AppStrings.Integrations.Adb.Actions.SendKeyName(),
				AppStrings.Integrations.Adb.Actions.SendKeyDescription(),
				_deviceDescription,
				[
					ActionParameter.Choice("key",
						KeyOptions(),
						AppStrings.Integrations.Adb.Params.KeyLabel(),
						defaultValue: nameof(AndroidKey.Home),
						required: true)
				],
				(target, parameters, ct) =>
				{
					var raw = AdbActionValues.ReadString(parameters, "key");
					return Enum.TryParse<AndroidKey>(raw, out var key)
						? target.RunAsync(device => device.SendKeyAsync(key, ct))
						: Task.FromResult(AdbOutcome.Fail(AndroidDeviceErrorCode.InvalidArgument, "Unknown key."));
				},
				ActionRiskCategory.Input),

			new AdbActionDefinition(services,
				"tap",
				AppStrings.Integrations.Adb.Actions.TapName(),
				AppStrings.Integrations.Adb.Actions.TapDescription(),
				_deviceDescription,
				[
					ActionParameter.Number("x",
						AppStrings.Integrations.Adb.Params.XLabel(),
						min: 0,
						required: true),
					ActionParameter.Number("y",
						AppStrings.Integrations.Adb.Params.YLabel(),
						min: 0,
						required: true)
				],
				(target, parameters, ct) =>
				{
					var x = AdbActionValues.ReadInt(parameters, "x", 0);
					var y = AdbActionValues.ReadInt(parameters, "y", 0);
					return target.RunAsync(device => device.TapAsync(x, y, ct));
				}),

			new AdbActionDefinition(services,
				"swipe",
				AppStrings.Integrations.Adb.Actions.SwipeName(),
				AppStrings.Integrations.Adb.Actions.SwipeDescription(),
				_deviceDescription,
				[
					ActionParameter.Number("x1",
						AppStrings.Integrations.Adb.Actions.SwipeFromXLabel(),
						min: 0,
						required: true),
					ActionParameter.Number("y1",
						AppStrings.Integrations.Adb.Actions.SwipeFromYLabel(),
						min: 0,
						required: true),
					ActionParameter.Number("x2",
						AppStrings.Integrations.Adb.Actions.SwipeToXLabel(),
						min: 0,
						required: true),
					ActionParameter.Number("y2",
						AppStrings.Integrations.Adb.Actions.SwipeToYLabel(),
						min: 0,
						required: true),
					ActionParameter.Number("durationMs",
						AppStrings.Integrations.Adb.Actions.SwipeDurationLabel(),
						min: 0,
						defaultValue: 300)
				],
				(target, parameters, ct) =>
				{
					var x1 = AdbActionValues.ReadInt(parameters, "x1", 0);
					var y1 = AdbActionValues.ReadInt(parameters, "y1", 0);
					var x2 = AdbActionValues.ReadInt(parameters, "x2", 0);
					var y2 = AdbActionValues.ReadInt(parameters, "y2", 0);
					var durationMs = AdbActionValues.ReadInt(parameters, "durationMs", 300);
					return target.RunAsync(device =>
						device.SwipeAsync(x1, y1, x2, y2, TimeSpan.FromMilliseconds(durationMs), ct));
				}),

			new AdbActionDefinition(services,
				"reboot",
				AppStrings.Integrations.Adb.Actions.RebootName(),
				AppStrings.Integrations.Adb.Actions.RebootDescription(),
				_deviceDescription,
				[
					ActionParameter.Choice("mode",
						[
							new ActionParameterOption
							{
								Value = nameof(AndroidRebootMode.Normal),
								Label = AppStrings.Integrations.Adb.Actions.RebootModeNormal()
							},
							new ActionParameterOption
							{
								Value = nameof(AndroidRebootMode.Recovery),
								Label = AppStrings.Integrations.Adb.Actions.RebootModeRecovery()
							},
							new ActionParameterOption
							{
								Value = nameof(AndroidRebootMode.Bootloader),
								Label = AppStrings.Integrations.Adb.Actions.RebootModeBootloader()
							}
						],
						AppStrings.Integrations.Adb.Actions.RebootModeLabel(),
						defaultValue: nameof(AndroidRebootMode.Normal))
				],
				(target, parameters, ct) =>
				{
					var raw = AdbActionValues.ReadString(parameters, "mode");
					var mode = Enum.TryParse<AndroidRebootMode>(raw, out var parsed)
						? parsed
						: AndroidRebootMode.Normal;
					return target.RunAsync(device => device.RebootAsync(mode, ct));
				},
				ActionRiskCategory.Power),

			new ScreenshotActionDefinition(services)
		];
	}

	private static Task<AdbOutcome> RequireText(
		IReadOnlyDictionary<string, object> parameters,
		string parameterName,
		string missingMessage,
		Func<string, Task<AdbOutcome>> whenPresent)
	{
		var value = AdbActionValues.ReadString(parameters, parameterName);
		return string.IsNullOrWhiteSpace(value)
			? Task.FromResult(AdbOutcome.Fail(AndroidDeviceErrorCode.InvalidArgument, missingMessage))
			: whenPresent(value);
	}

	private static List<ActionParameterOption> KeyOptions()
	{
		return Enum.GetValues<AndroidKey>()
			.Select(key => new ActionParameterOption
				{ Value = key.ToString(), Label = Humanize(key.ToString()) })
			.ToList();
	}

	private static string Humanize(string pascalCase)
	{
		var builder = new StringBuilder(pascalCase.Length + 4);
		foreach (var character in pascalCase)
		{
			if (char.IsUpper(character) && builder.Length > 0)
			{
				builder.Append(' ');
			}

			builder.Append(character);
		}

		return builder.ToString();
	}
}
