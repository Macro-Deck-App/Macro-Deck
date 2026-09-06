using System.Text;
using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.Adb.Actions;

internal static class AdbActions
{
	private static readonly LocalizedText DeviceDescription = AppStrings.Integrations.Adb.Params.DeviceDescription();

	public static IReadOnlyList<IActionDefinition> Create(
		Func<IAdbGateway?> resolveGateway,
		AdbHealthTracker health,
		VariableApiAccessor variables)
		=>
		[
			new AdbActionDefinition(resolveGateway,
				health,
				"wake",
				AppStrings.Integrations.Adb.Actions.WakeName(),
				AppStrings.Integrations.Adb.Actions.WakeDescription(),
				DeviceDescription,
				[],
				(gateway, device, parameters, ct) => gateway.SendKeyAsync(device, AdbGatewayKey.Wakeup, ct)),

			new AdbActionDefinition(resolveGateway,
				health,
				"sleep",
				AppStrings.Integrations.Adb.Actions.SleepName(),
				AppStrings.Integrations.Adb.Actions.SleepDescription(),
				DeviceDescription,
				[],
				(gateway, device, parameters, ct) => gateway.SendKeyAsync(device, AdbGatewayKey.Sleep, ct)),

			new AdbActionDefinition(resolveGateway,
				health,
				"start-app",
				AppStrings.Integrations.Adb.Actions.StartAppName(),
				AppStrings.Integrations.Adb.Actions.StartAppDescription(),
				DeviceDescription,
				[
					ActionParameter.Text("package",
						label: AppStrings.Integrations.Adb.Params.PackageLabel(),
						placeholder: "com.example.app",
						required: true)
				],
				(gateway, device, parameters, ct) => RequireText(parameters,
					"package",
					"A package name is required.",
					package => gateway.StartAppAsync(device, package, ct))),

			new AdbActionDefinition(resolveGateway,
				health,
				"stop-app",
				AppStrings.Integrations.Adb.Actions.StopAppName(),
				AppStrings.Integrations.Adb.Actions.StopAppDescription(),
				DeviceDescription,
				[
					ActionParameter.Text("package",
						label: AppStrings.Integrations.Adb.Params.PackageLabel(),
						placeholder: "com.example.app",
						required: true)
				],
				(gateway, device, parameters, ct) => RequireText(parameters,
					"package",
					"A package name is required.",
					package => gateway.ForceStopAppAsync(device, package, ct))),

			new AdbActionDefinition(resolveGateway,
				health,
				"open-uri",
				AppStrings.Integrations.Adb.Actions.OpenLinkName(),
				AppStrings.Integrations.Adb.Actions.OpenLinkDescription(),
				DeviceDescription,
				[
					ActionParameter.Text("uri",
						label: AppStrings.Integrations.Adb.Params.UriLabel(),
						placeholder: "https://example.com",
						required: true)
				],
				(gateway, device, parameters, ct) => RequireText(parameters,
					"uri",
					"A URI is required.",
					uri => gateway.OpenUriAsync(device, uri, ct))),

			new AdbActionDefinition(resolveGateway,
				health,
				"input-text",
				AppStrings.Integrations.Adb.Actions.TypeTextName(),
				AppStrings.Integrations.Adb.Actions.TypeTextDescription(),
				DeviceDescription,
				[
					ActionParameter.Text("text",
						label: AppStrings.Integrations.Adb.Params.TextLabel(),
						required: true)
				],
				(gateway, device, parameters, ct) => RequireText(parameters,
					"text",
					"Text is required.",
					text => gateway.InputTextAsync(device, text, ct))),

			new AdbActionDefinition(resolveGateway,
				health,
				"key-event",
				AppStrings.Integrations.Adb.Actions.SendKeyName(),
				AppStrings.Integrations.Adb.Actions.SendKeyDescription(),
				DeviceDescription,
				[
					ActionParameter.Choice("key",
						options: KeyOptions(),
						label: AppStrings.Integrations.Adb.Params.KeyLabel(),
						defaultValue: nameof(AdbGatewayKey.Home),
						required: true)
				],
				(gateway, device, parameters, ct) =>
				{
					var raw = AdbActionValues.ReadString(parameters, "key");
					return Enum.TryParse<AdbGatewayKey>(raw, out var key)
						? gateway.SendKeyAsync(device, key, ct)
						: Task.FromResult(AdbGatewayResult.Fail(AdbGatewayFailureCode.InvalidParameter,
							"Unknown key."));
				}),

			new AdbActionDefinition(resolveGateway,
				health,
				"tap",
				AppStrings.Integrations.Adb.Actions.TapName(),
				AppStrings.Integrations.Adb.Actions.TapDescription(),
				DeviceDescription,
				[
					ActionParameter.Number("x",
						label: AppStrings.Integrations.Adb.Params.XLabel(),
						min: 0,
						required: true),
					ActionParameter.Number("y",
						label: AppStrings.Integrations.Adb.Params.YLabel(),
						min: 0,
						required: true)
				],
				(gateway, device, parameters, ct) =>
				{
					var x = AdbActionValues.ReadInt(parameters, "x", 0);
					var y = AdbActionValues.ReadInt(parameters, "y", 0);
					return gateway.TapAsync(device, x, y, ct);
				}),

			new AdbActionDefinition(resolveGateway,
				health,
				"swipe",
				AppStrings.Integrations.Adb.Actions.SwipeName(),
				AppStrings.Integrations.Adb.Actions.SwipeDescription(),
				DeviceDescription,
				[
					ActionParameter.Number("x1",
						label: AppStrings.Integrations.Adb.Actions.SwipeFromXLabel(),
						min: 0,
						required: true),
					ActionParameter.Number("y1",
						label: AppStrings.Integrations.Adb.Actions.SwipeFromYLabel(),
						min: 0,
						required: true),
					ActionParameter.Number("x2",
						label: AppStrings.Integrations.Adb.Actions.SwipeToXLabel(),
						min: 0,
						required: true),
					ActionParameter.Number("y2",
						label: AppStrings.Integrations.Adb.Actions.SwipeToYLabel(),
						min: 0,
						required: true),
					ActionParameter.Number("durationMs",
						label: AppStrings.Integrations.Adb.Actions.SwipeDurationLabel(),
						min: 0,
						defaultValue: 300)
				],
				(gateway, device, parameters, ct) =>
				{
					var x1 = AdbActionValues.ReadInt(parameters, "x1", 0);
					var y1 = AdbActionValues.ReadInt(parameters, "y1", 0);
					var x2 = AdbActionValues.ReadInt(parameters, "x2", 0);
					var y2 = AdbActionValues.ReadInt(parameters, "y2", 0);
					var durationMs = AdbActionValues.ReadInt(parameters, "durationMs", 300);
					return gateway.SwipeAsync(device, x1, y1, x2, y2, durationMs, ct);
				}),

			new AdbActionDefinition(resolveGateway,
				health,
				"reboot",
				AppStrings.Integrations.Adb.Actions.RebootName(),
				AppStrings.Integrations.Adb.Actions.RebootDescription(),
				DeviceDescription,
				[
					ActionParameter.Choice("mode",
						options:
						[
							new ActionParameterOption
							{
								Value = nameof(AdbGatewayRebootMode.Normal),
								Label = AppStrings.Integrations.Adb.Actions.RebootModeNormal()
							},
							new ActionParameterOption
							{
								Value = nameof(AdbGatewayRebootMode.Recovery),
								Label = AppStrings.Integrations.Adb.Actions.RebootModeRecovery()
							},
							new ActionParameterOption
							{
								Value = nameof(AdbGatewayRebootMode.Bootloader),
								Label = AppStrings.Integrations.Adb.Actions.RebootModeBootloader()
							}
						],
						label: AppStrings.Integrations.Adb.Actions.RebootModeLabel(),
						defaultValue: nameof(AdbGatewayRebootMode.Normal))
				],
				(gateway, device, parameters, ct) =>
				{
					var raw = AdbActionValues.ReadString(parameters, "mode");
					var mode = Enum.TryParse<AdbGatewayRebootMode>(raw, out var parsed)
						? parsed
						: AdbGatewayRebootMode.Normal;
					return gateway.RebootAsync(device, mode, ct);
				}),

			new ScreenshotActionDefinition(resolveGateway, health, variables)
		];

	private static Task<AdbGatewayResult> RequireText(
		IReadOnlyDictionary<string, object> parameters,
		string parameterName,
		string missingMessage,
		Func<string, Task<AdbGatewayResult>> whenPresent)
	{
		var value = AdbActionValues.ReadString(parameters, parameterName);
		return string.IsNullOrWhiteSpace(value)
			? Task.FromResult(AdbGatewayResult.Fail(AdbGatewayFailureCode.InvalidParameter, missingMessage))
			: whenPresent(value);
	}

	private static List<ActionParameterOption> KeyOptions()
		=> Enum.GetValues<AdbGatewayKey>()
			.Select(key => new ActionParameterOption
				{ Value = key.ToString(), Label = Humanize(key.ToString()) })
			.ToList();

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
