using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Weather;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Weather;

/// <summary>
/// Opens the Weather detail dialog on whichever client ran the action - the first-party worked example
/// of an action that starts an interaction instead of doing something and returning.
/// </summary>
/// <remarks>
/// Fire-and-forget by design: this dialog shows information rather than asking a question, so the action
/// finishes as soon as the modal is open. An action that needed an answer would await
/// <see cref="IUiInteractions.ShowModalAsync{T}" /> instead and check <c>Cancelled</c> before using it.
/// </remarks>
internal sealed class WeatherDetailsActionDefinition : IDynamicOptionsActionDefinition
{
	internal const string InstanceParameter = "instanceId";

	/// <summary>Must match <c>WeatherDetailsUiProvider.DetailsViewId</c>, which serves the dialog.</summary>
	private const string DetailsViewId = "weather-details";

	private readonly Func<IReadOnlyList<WeatherStationInstance>> _instances;

	public WeatherDetailsActionDefinition(Func<IReadOnlyList<WeatherStationInstance>> instances)
	{
		_instances = instances;
	}

	public string Id => "show-details";

	public LocalizedText Name => AppStrings.Integrations.Weather.Details.ActionName();

	public LocalizedText Description => AppStrings.Integrations.Weather.Details.ActionDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.DynamicChoice(InstanceParameter,
			label: AppStrings.Integrations.Weather.Details.LocationLabel(),
			required: false)
	];

	public IActionExecutor CreateExecutor() => new Executor();

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);

		var options = _instances()
			.Where(instance => context.Filter is null ||
				instance.DisplayName.Contains(context.Filter, StringComparison.OrdinalIgnoreCase))
			// The stored value is resolved through the host's weather registry, which keys stations by
			// their qualified id - offering the bare local id here yields a value nothing can look up,
			// and the dialog falls back to its no-location empty state on a location the user did pick.
			.Select(instance => QualifiedId.TryCreate(WeatherIntegration.IntegrationId, instance.Id, out var id)
				? new ActionParameterOption { Value = id.ToString(), Label = instance.DisplayName }
				: null)
			.OfType<ActionParameterOption>()
			.ToList();

		return Task.FromResult(new DynamicOptionsResult
		{
			Options = options, AllowsCustomValue = false, CacheSeconds = 5
		});
	}

	private sealed class Executor : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			ArgumentNullException.ThrowIfNull(context);

			if (context.Ui is null)
			{
				// A backend-initiated run has nobody to show a dialog to. Not a failure: the action did
				// everything it could, and failing here would turn an automation into a red flow.
				return ActionResult.Success();
			}

			var instanceId = context.Parameters.TryGetValue(InstanceParameter, out var value)
				? value.ToString()
				: null;

			var data = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
			if (!string.IsNullOrEmpty(instanceId))
			{
				data[InstanceParameter] = JsonSerializer.SerializeToElement(instanceId);
			}

			await context.Ui.ShowModalAsync(context.OriginClientId,
					new ModalDefinition
					{
						ViewId = DetailsViewId,
						Title = AppStrings.Integrations.Weather.Details.Title(),
						Data = data
					},
					context.CancellationToken)
				.ConfigureAwait(false);

			return ActionResult.Success();
		}
	}
}
