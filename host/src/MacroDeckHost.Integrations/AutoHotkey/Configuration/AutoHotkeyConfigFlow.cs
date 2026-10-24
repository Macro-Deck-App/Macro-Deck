using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Integrations.AutoHotkey.Native;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.AutoHotkey.Config;

namespace MacroDeckHost.Integrations.AutoHotkey.Configuration;

internal sealed class AutoHotkeyConfigFlow : IConfigFlow
{
	private const string SettingsStepId = "settings";
	private const double GraceStepSeconds = 0.5;

	private readonly Func<string?, CancellationToken, Task<AutoHotkeyInstallation>> _detect;
	private readonly Func<CancellationToken, Task<AutoHotkeySettings?>> _load;
	private readonly Func<AutoHotkeySettings, CancellationToken, Task> _save;

	public AutoHotkeyConfigFlow(
		Func<string?, CancellationToken, Task<AutoHotkeyInstallation>> detect,
		Func<CancellationToken, Task<AutoHotkeySettings?>> load,
		Func<AutoHotkeySettings, CancellationToken, Task> save)
	{
		_detect = detect;
		_load = load;
		_save = save;
	}

	public async Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
	{
		return ConfigFlowResult.Step(await StepAsync(cancellationToken).ConfigureAwait(false));
	}

	// The settings live in the integration's storage and end the flow without a config entry: the host
	// re-initialises an integration after an entry is stored, which would stop the running scripts.
	public async Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
	{
		if (stepId != SettingsStepId)
		{
			return ConfigFlowResult.Error(await StepAsync(cancellationToken).ConfigureAwait(false),
				Strings.UnknownStep());
		}

		if (await _load(cancellationToken).ConfigureAwait(false) is null)
		{
			return ConfigFlowResult.Error(await StepAsync(cancellationToken).ConfigureAwait(false),
				Strings.EnableFirst());
		}

		var manual = string.Equals(Text(input, AutoHotkeyConfigKeys.ExecutableMode),
			AutoHotkeyConfigKeys.ManualMode,
			StringComparison.Ordinal);
		var path = Text(input, AutoHotkeyConfigKeys.ExecutablePath)?.Trim().Trim('"');
		var grace
			= AutoHotkeySettings.ParseGracePeriodSeconds(input.GetValueOrDefault(AutoHotkeyConfigKeys.GracePeriod));

		if (manual && await ManualPathErrorAsync(path, cancellationToken).ConfigureAwait(false) is { } message)
		{
			return ConfigFlowResult.Error(await StepAsync(cancellationToken).ConfigureAwait(false),
				fieldErrors: new Dictionary<string, LocalizedText>(StringComparer.Ordinal)
				{
					[AutoHotkeyConfigKeys.ExecutablePath] = message
				});
		}

		await _save(new AutoHotkeySettings(manual ? path : null, grace), cancellationToken).ConfigureAwait(false);
		return ConfigFlowResult.CompleteWithoutEntry();
	}

	private async Task<LocalizedText?> ManualPathErrorAsync(string? path, CancellationToken cancellationToken)
	{
		if (string.IsNullOrEmpty(path)
			|| !Path.IsPathFullyQualified(path)
			|| !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
		{
			return Strings.ExecutableInvalid();
		}

		var chosen = await _detect(path, cancellationToken).ConfigureAwait(false);
		return chosen.Status switch
		{
			AutoHotkeyInstallationStatus.Valid => null,
			AutoHotkeyInstallationStatus.NotFound => Strings.ExecutableNotFound(),
			AutoHotkeyInstallationStatus.NotVersion2 => Strings.ExecutableNotVersion2(),
			_ => Strings.ExecutableDoesNotRun()
		};
	}

	private static string? Text(IReadOnlyDictionary<string, object?> input, string key)
	{
		return input.GetValueOrDefault(key) switch
		{
			null => null,
			string text => text,
			var other => other.ToString()
		};
	}

	private async Task<ConfigFlowStep> StepAsync(CancellationToken cancellationToken)
	{
		var stored = await _load(cancellationToken).ConfigureAwait(false);
		var settings = stored ?? AutoHotkeySettings.Default;
		var inUse = await _detect(settings.ManualExecutable, cancellationToken).ConfigureAwait(false);

		var values = new List<ConfigFlowCopyValue>();
		var instructions = new List<ConfigFlowInstruction>();
		if (stored is null)
		{
			instructions.Add(new ConfigFlowInstruction { Text = Strings.EnableFirst() });
		}

		if (inUse.IsValid)
		{
			values.Add(new ConfigFlowCopyValue
			{
				Label = Strings.DetectedExecutableLabel(), Value = inUse.ExecutablePath!
			});
			values.Add(new ConfigFlowCopyValue
			{
				Label = Strings.DetectedVersionLabel(), Value = inUse.Version ?? string.Empty
			});
		}
		else
		{
			instructions.Add(new ConfigFlowInstruction { Text = Strings.NotDetected() });
		}

		return new ConfigFlowStep
		{
			StepId = SettingsStepId,
			Title = Strings.Title(),
			Description = Strings.Description(),
			Values = values,
			Instructions = instructions,
			Links = [new ConfigFlowLink { Label = Strings.DownloadLinkLabel(), Url = AutoHotkeyLinks.Download }],
			Fields =
			[
				ActionParameter.Choice(AutoHotkeyConfigKeys.ExecutableMode,
					[
						new ActionParameterOption
						{
							Value = AutoHotkeyConfigKeys.AutomaticMode, Label = Strings.AutomaticOption()
						},
						new ActionParameterOption
						{
							Value = AutoHotkeyConfigKeys.ManualMode, Label = Strings.ManualOption()
						}
					],
					Strings.ExecutableModeLabel(),
					defaultValue: settings.ManualExecutable is null
						? AutoHotkeyConfigKeys.AutomaticMode
						: AutoHotkeyConfigKeys.ManualMode),
				new ActionParameter
				{
					Name = AutoHotkeyConfigKeys.ExecutablePath,
					Type = ActionParameterType.File,
					FileExtensions = ["exe"],
					Label = Strings.ExecutablePathLabel(),
					Description = Strings.ExecutablePathDescription(),
					DefaultValue = settings.ManualExecutable
				}.OnlyWhen(AutoHotkeyConfigKeys.ExecutableMode, AutoHotkeyConfigKeys.ManualMode),
				ActionParameter.Number(AutoHotkeyConfigKeys.GracePeriod,
					Strings.GracePeriodLabel(),
					Strings.GracePeriodDescription(),
					AutoHotkeySettings.MinGracePeriod.TotalSeconds,
					AutoHotkeySettings.MaxGracePeriod.TotalSeconds,
					GraceStepSeconds,
					settings.GracePeriod.TotalSeconds)
			]
		};
	}
}
