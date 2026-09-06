using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Example;

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
			label: AppStrings.Integrations.Example.Actions.TitleLabel(),
			description: AppStrings.Integrations.Example.Actions.TitleDescription(),
			placeholder: AppStrings.Integrations.Example.Actions.TitlePlaceholder(),
			required: true),
		ActionParameter.MultilineText("body",
			label: AppStrings.Integrations.Example.Actions.BodyLabel(),
			description: AppStrings.Integrations.Example.Actions.BodyDescription(),
			placeholder: AppStrings.Integrations.Example.Actions.BodyPlaceholder()),
		ActionParameter.Number("amount",
			label: AppStrings.Integrations.Example.Actions.AmountLabel(),
			description: AppStrings.Integrations.Example.Actions.AmountDescription(),
			min: 0,
			max: 1000,
			step: 0.5,
			defaultValue: 10),
		ActionParameter.Slider("volume",
			min: 0,
			max: 100,
			step: 1,
			label: AppStrings.Integrations.Example.Actions.VolumeLabel(),
			description: AppStrings.Integrations.Example.Actions.VolumeDescription(),
			defaultValue: 50),
		ActionParameter.Toggle("enabled",
			label: AppStrings.Integrations.Example.Actions.EnabledLabel(),
			description: AppStrings.Integrations.Example.Actions.EnabledDescription(),
			defaultValue: true),
		ActionParameter.Password("apiKey",
			label: AppStrings.Integrations.Example.Actions.ApiKeyLabel(),
			description: AppStrings.Integrations.Example.Actions.ApiKeyDescription()),
		ActionParameter.Secret("refreshToken",
			label: AppStrings.Integrations.Example.Actions.RefreshTokenLabel(),
			description: AppStrings.Integrations.Example.Actions.RefreshTokenDescription()),
		ActionParameter.Choice("deviceCategory",
			options:
			[
				new ActionParameterOption
					{ Value = "input", Label = AppStrings.Integrations.Example.Actions.InputDevicesLabel() },
				new ActionParameterOption
					{ Value = "output", Label = AppStrings.Integrations.Example.Actions.OutputDevicesLabel() }
			],
			label: AppStrings.Integrations.Example.Actions.DeviceCategoryLabel(),
			description: AppStrings.Integrations.Example.Actions.DeviceCategoryDescription(),
			defaultValue: "output"),
		ActionParameter.DynamicChoice("device",
			label: AppStrings.Integrations.Example.Actions.DeviceLabel(),
			description: AppStrings.Integrations.Example.Actions.DeviceDescription()),
		ActionParameter.Autocomplete("process",
			label: AppStrings.Integrations.Example.Actions.ProcessLabel(),
			description: AppStrings.Integrations.Example.Actions.ProcessDescription(),
			optionsSourceId: "system.processes",
			placeholder: AppStrings.Integrations.Example.Actions.ProcessPlaceholder()),
		ActionParameter.MultiSelect("tags",
			options:
			[
				new ActionParameterOption { Value = "alpha" },
				new ActionParameterOption { Value = "beta" },
				new ActionParameterOption { Value = "gamma" }
			],
			label: AppStrings.Integrations.Example.Actions.TagsLabel(),
			description: AppStrings.Integrations.Example.Actions.TagsDescription()),
		ActionParameter.Color("highlight",
			label: AppStrings.Integrations.Example.Actions.HighlightColorLabel(),
			defaultValue: "#3b82f6"),
		ActionParameter.File("soundFile",
			label: AppStrings.Integrations.Example.Actions.SoundFileLabel(),
			description: AppStrings.Integrations.Example.Actions.SoundFileDescription(),
			fileExtensions: ["mp3", "wav", "ogg"]),
		ActionParameter.Folder("outputFolder",
			label: AppStrings.Integrations.Example.Actions.OutputFolderLabel()),
		ActionParameter.Hotkey("shortcut",
			label: AppStrings.Integrations.Example.Actions.ShortcutLabel(),
			description: AppStrings.Integrations.Example.Actions.ShortcutDescription()),
		ActionParameter.Duration("delay",
			label: AppStrings.Integrations.Example.Actions.DelayLabel(),
			description: AppStrings.Integrations.Example.Actions.DelayDescription(),
			defaultMilliseconds: 500),
		ActionParameter.DateTime("scheduledAt",
			label: AppStrings.Integrations.Example.Actions.ScheduledAtLabel()),
		ActionParameter.Json("payload",
			label: AppStrings.Integrations.Example.Actions.PayloadLabel(),
			description: AppStrings.Integrations.Example.Actions.PayloadDescription(),
			defaultValue: "{\n  \"key\": \"value\"\n}"),
		ActionParameter.Code("script",
			language: "javascript",
			label: AppStrings.Integrations.Example.Actions.ScriptLabel(),
			description: AppStrings.Integrations.Example.Actions.ScriptDescription()),
		ActionParameter.KeyValue("headers",
			label: AppStrings.Integrations.Example.Actions.HeadersLabel(),
			description: AppStrings.Integrations.Example.Actions.HeadersDescription()),
		ActionParameter.Object("advanced",
			children:
			[
				ActionParameter.Text("host", label: AppStrings.Integrations.Example.Actions.HostLabel()),
				ActionParameter.Number("port",
					label: AppStrings.Integrations.Example.Actions.PortLabel(),
					min: 1,
					max: 65535,
					defaultValue: 8080)
			],
			label: AppStrings.Integrations.Example.Actions.AdvancedLabel(),
			description: AppStrings.Integrations.Example.Actions.AdvancedDescription()),
		ActionParameter.Array("urls",
			itemTemplate: ActionParameter.Url("url",
				label: AppStrings.Integrations.Example.Actions.UrlLabel(),
				placeholder: "https://…"),
			label: AppStrings.Integrations.Example.Actions.UrlsLabel(),
			description: AppStrings.Integrations.Example.Actions.UrlsDescription()),
		ActionParameter.IpAddress("deviceIp",
			label: AppStrings.Integrations.Example.Actions.DeviceIpLabel()),
		ActionParameter.Url("webhookUrl",
			label: AppStrings.Integrations.Example.Actions.WebhookUrlLabel(),
			placeholder: "https://example.com/hook"),
		ActionParameter.Icon("buttonIcon",
			label: AppStrings.Integrations.Example.Actions.ButtonIconLabel()),
		ActionParameter.Image("buttonImage",
			label: AppStrings.Integrations.Example.Actions.ButtonImageLabel())
	];

	public IActionExecutor CreateExecutor() => new ParameterShowcaseActionExecutor();

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
