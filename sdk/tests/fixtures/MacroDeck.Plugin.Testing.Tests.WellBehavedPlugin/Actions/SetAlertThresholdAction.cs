using System.Text.Json;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Ui;
using MacroDeck.Localization;
using MacroDeck.Plugin.Testing.Tests.WellBehavedPlugin.ConfigFlow;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;

namespace MacroDeck.Plugin.Testing.Tests.WellBehavedPlugin.Actions;

/// <summary>
/// Sets the temperature above which the next <c>weather-refreshed</c> event reports <c>isAlert</c> - the
/// same value <c>sample_alert_threshold_celsius</c> exposes as a writable variable, so the fixture covers
/// both ways of reaching one setting.
///
/// <para>
/// Also implements <see cref="IUiConfigurableActionDefinition"/>: <see cref="CreateConfigurationSessionAsync"/>
/// renders the two declared <see cref="Parameters"/> - the threshold and a webhook signing secret - as a
/// tree seeded from the configured instance's stored values, while <see cref="Parameters"/> itself stays
/// exactly what a client without tree support persists into. The host masks
/// <see cref="WebhookSecretParameterName"/>'s stored value before it ever reaches
/// <see cref="ActionConfigurationRequest.Parameters"/> here, so this method never sees the real secret it
/// is seeding a field with.
/// </para>
/// </summary>
internal sealed class SetAlertThresholdAction(WellBehavedIntegration integration)
	: IActionDefinition, IUiConfigurableActionDefinition
{
	private const double Min = WellBehavedIntegration.AlertThresholdMin;
	private const double Max = WellBehavedIntegration.AlertThresholdMax;

	internal const string WebhookSecretParameterName = "webhookSecret";

	public string Id => "set-alert-threshold";

	public LocalizedText Name => "Set alert threshold";

	public LocalizedText Description =>
		"Sets the temperature (°C) above which weather-refreshed events report isAlert.";

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Slider("thresholdCelsius", Min, Max, label: "Alert threshold (°C)", step: 1, defaultValue: 30),
		ActionParameter.Secret(WebhookSecretParameterName, label: "Webhook signing secret")
	];

	public IActionExecutor CreateExecutor() => new Executor(integration);

	public Task<IUiSession?> CreateConfigurationSessionAsync(
		ActionConfigurationRequest request,
		CancellationToken cancellationToken)
	{
		var view = new UiView(request.Session.Surface,
			BuildTree(ReadDouble(request.Parameters, "thresholdCelsius", 30),
				ReadString(request.Parameters, WebhookSecretParameterName)));

		return Task.FromResult<IUiSession?>(new WellBehavedUiSession(view));
	}

	// Shared with SetAlertThresholdActionPreviews so a preview renders the tree this action really
	// serves rather than a copy of it that can drift.
	internal static UiElement BuildTree(double threshold, string webhookSecret) => new UiStep
	{
		Key = "set-alert-threshold",
		StepId = UiValue.Of("set-alert-threshold"),
		Title = "Alert threshold",
		Description = "Sets the temperature above which weather-refreshed events report isAlert.",
		Children =
		[
			new UiNumberInput
			{
				Key = "thresholdCelsius",
				Label = "Alert threshold (°C)",
				Min = Min,
				Max = Max,
				Step = 1,
				ShowSlider = true,
				Binding = Bind.To(new UiState<double>(threshold))
			},
			new UiSecretInput
			{
				Key = WebhookSecretParameterName,
				Label = "Webhook signing secret",
				Binding = Bind.To(new UiState<string>(webhookSecret))
			}
		]
	};

	private static double ReadDouble(IReadOnlyDictionary<string, JsonElement> parameters, string name, double fallback)
		=> parameters.TryGetValue(name, out var element) && element.ValueKind == JsonValueKind.Number
			? element.GetDouble()
			: fallback;

	private static string ReadString(IReadOnlyDictionary<string, JsonElement> parameters, string name)
		=> parameters.TryGetValue(name, out var element) && element.ValueKind == JsonValueKind.String
			? element.GetString() ?? string.Empty
			: string.Empty;

	private sealed class Executor(WellBehavedIntegration integration) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (context.Parameters.GetValueOrDefault("thresholdCelsius") is not double threshold)
			{
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					"thresholdCelsius must be a number."));
			}

			integration.AlertThresholdCelsius = threshold;
			return ActionResult.SucceededTask;
		}
	}
}
