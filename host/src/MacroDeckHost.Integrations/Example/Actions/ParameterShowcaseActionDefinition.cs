using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Example.Actions;

public class ParameterShowcaseActionDefinition : IDynamicOptionsActionDefinition
{
	private static readonly string[] _inputDevices = ["Microphone (USB)", "Line In", "Webcam Mic"];
	private static readonly string[] _outputDevices = ["Speakers", "Headphones", "HDMI Audio"];

	public string Id => "parameter-showcase";
	public LocalizedText Name => AppStrings.Integrations.Example.Actions.ShowcaseName();
	public LocalizedText Description => AppStrings.Integrations.Example.Actions.ShowcaseDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Text("title",
			AppStrings.Integrations.Example.Actions.TitleLabel(),
			AppStrings.Integrations.Example.Actions.TitleDescription(),
			AppStrings.Integrations.Example.Actions.TitlePlaceholder(),
			required: true),
		ActionParameter.MultilineText("body",
			AppStrings.Integrations.Example.Actions.BodyLabel(),
			AppStrings.Integrations.Example.Actions.BodyDescription(),
			AppStrings.Integrations.Example.Actions.BodyPlaceholder()),
		ActionParameter.Number("amount",
			AppStrings.Integrations.Example.Actions.AmountLabel(),
			AppStrings.Integrations.Example.Actions.AmountDescription(),
			0,
			1000,
			0.5,
			10),
		ActionParameter.Slider("volume",
			0,
			100,
			step: 1,
			label: AppStrings.Integrations.Example.Actions.VolumeLabel(),
			description: AppStrings.Integrations.Example.Actions.VolumeDescription(),
			defaultValue: 50),
		ActionParameter.Toggle("enabled",
			AppStrings.Integrations.Example.Actions.EnabledLabel(),
			AppStrings.Integrations.Example.Actions.EnabledDescription(),
			true),
		ActionParameter.Password("apiKey",
			AppStrings.Integrations.Example.Actions.ApiKeyLabel(),
			AppStrings.Integrations.Example.Actions.ApiKeyDescription()),
		ActionParameter.Secret("refreshToken",
			AppStrings.Integrations.Example.Actions.RefreshTokenLabel(),
			AppStrings.Integrations.Example.Actions.RefreshTokenDescription()),
		ActionParameter.Choice("deviceCategory",
			[
				new ActionParameterOption
					{ Value = "input", Label = AppStrings.Integrations.Example.Actions.InputDevicesLabel() },
				new ActionParameterOption
					{ Value = "output", Label = AppStrings.Integrations.Example.Actions.OutputDevicesLabel() }
			],
			AppStrings.Integrations.Example.Actions.DeviceCategoryLabel(),
			AppStrings.Integrations.Example.Actions.DeviceCategoryDescription(),
			"output"),
		ActionParameter.DynamicChoice("device",
			AppStrings.Integrations.Example.Actions.DeviceLabel(),
			AppStrings.Integrations.Example.Actions.DeviceDescription()),
		ActionParameter.Autocomplete("process",
			AppStrings.Integrations.Example.Actions.ProcessLabel(),
			AppStrings.Integrations.Example.Actions.ProcessDescription(),
			optionsSourceId: "system.processes",
			placeholder: AppStrings.Integrations.Example.Actions.ProcessPlaceholder()),
		ActionParameter.MultiSelect("tags",
			[
				new ActionParameterOption { Value = "alpha" },
				new ActionParameterOption { Value = "beta" },
				new ActionParameterOption { Value = "gamma" }
			],
			AppStrings.Integrations.Example.Actions.TagsLabel(),
			AppStrings.Integrations.Example.Actions.TagsDescription()),
		ActionParameter.Color("highlight",
			AppStrings.Integrations.Example.Actions.HighlightColorLabel(),
			defaultValue: "#3b82f6"),
		ActionParameter.File("soundFile",
			AppStrings.Integrations.Example.Actions.SoundFileLabel(),
			AppStrings.Integrations.Example.Actions.SoundFileDescription(),
			["mp3", "wav", "ogg"]),
		ActionParameter.Folder("outputFolder",
			AppStrings.Integrations.Example.Actions.OutputFolderLabel()),
		ActionParameter.Hotkey("shortcut",
			AppStrings.Integrations.Example.Actions.ShortcutLabel(),
			AppStrings.Integrations.Example.Actions.ShortcutDescription()),
		ActionParameter.Duration("delay",
			AppStrings.Integrations.Example.Actions.DelayLabel(),
			AppStrings.Integrations.Example.Actions.DelayDescription(),
			defaultMilliseconds: 500),
		ActionParameter.DateTime("scheduledAt",
			AppStrings.Integrations.Example.Actions.ScheduledAtLabel()),
		ActionParameter.Json("payload",
			AppStrings.Integrations.Example.Actions.PayloadLabel(),
			AppStrings.Integrations.Example.Actions.PayloadDescription(),
			"{\n  \"key\": \"value\"\n}"),
		ActionParameter.Code("script",
			"javascript",
			AppStrings.Integrations.Example.Actions.ScriptLabel(),
			AppStrings.Integrations.Example.Actions.ScriptDescription()),
		ActionParameter.KeyValue("headers",
			AppStrings.Integrations.Example.Actions.HeadersLabel(),
			AppStrings.Integrations.Example.Actions.HeadersDescription()),
		ActionParameter.Object("advanced",
			[
				ActionParameter.Text("host", AppStrings.Integrations.Example.Actions.HostLabel()),
				ActionParameter.Number("port",
					AppStrings.Integrations.Example.Actions.PortLabel(),
					min: 1,
					max: 65535,
					defaultValue: 8080)
			],
			AppStrings.Integrations.Example.Actions.AdvancedLabel(),
			AppStrings.Integrations.Example.Actions.AdvancedDescription()),
		ActionParameter.Array("urls",
			ActionParameter.Url("url",
				AppStrings.Integrations.Example.Actions.UrlLabel(),
				placeholder: "https://…"),
			AppStrings.Integrations.Example.Actions.UrlsLabel(),
			AppStrings.Integrations.Example.Actions.UrlsDescription()),
		ActionParameter.IpAddress("deviceIp",
			AppStrings.Integrations.Example.Actions.DeviceIpLabel()),
		ActionParameter.Url("webhookUrl",
			AppStrings.Integrations.Example.Actions.WebhookUrlLabel(),
			placeholder: "https://example.com/hook"),
		ActionParameter.Icon("buttonIcon",
			AppStrings.Integrations.Example.Actions.ButtonIconLabel()),
		ActionParameter.Image("buttonImage",
			AppStrings.Integrations.Example.Actions.ButtonImageLabel())
	];

	public IActionExecutor CreateExecutor()
	{
		return new ParameterShowcaseActionExecutor();
	}

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		var category = context.CurrentParameters.TryGetValue("deviceCategory", out var value)
			? value?.ToString()
			: null;

		var devices = category == "input" ? _inputDevices : _outputDevices;
		var options = devices
			.Where(device =>
				context.Filter is null || device.Contains(context.Filter, StringComparison.OrdinalIgnoreCase))
			.Select(device => new ActionParameterOption { Value = device })
			.ToList();

		return Task.FromResult(new DynamicOptionsResult
		{
			Options = options,
			CacheSeconds = 10
		});
	}

	private sealed class ParameterShowcaseActionExecutor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<ParameterShowcaseActionExecutor>(ExampleIntegration.IntegrationId);

		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			foreach (var (name, value) in context.Parameters.OrderBy(p => p.Key))
			{
				var display = value switch
				{
					string[] array => string.Join(", ", array),
					IReadOnlyDictionary<string, string> map
						=> string.Join(", ", map.Select(pair => $"{pair.Key}={pair.Value}")),
					_ => value.ToString()
				};

				_logger.Information("[ParameterShowcase] {Name} ({Type}) = {Value}",
					name,
					value.GetType().Name,
					display);
			}

			return ActionResult.SucceededTask;
		}
	}
}
