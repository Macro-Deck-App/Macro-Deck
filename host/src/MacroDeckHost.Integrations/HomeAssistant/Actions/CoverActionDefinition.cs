using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.HomeAssistant.Actions;

internal sealed class CoverActionDefinition : IDynamicOptionsActionDefinition
{
	internal const string EntityParameterName = "entity";
	internal const string CommandParameterName = "command";
	internal const string PositionParameterName = "position";
	internal const string TiltPositionParameterName = "tiltPosition";

	private const string CoverDomain = "cover";

	private readonly Func<HomeAssistantConnection?> _resolver;

	public CoverActionDefinition(Func<HomeAssistantConnection?> resolver)
	{
		_resolver = resolver;
	}

	public string Id => "cover-set";

	public LocalizedText Name => AppStrings.Integrations.HomeAssistant.Actions.Cover.Name();

	public LocalizedText Description => AppStrings.Integrations.HomeAssistant.Actions.Cover.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Autocomplete(EntityParameterName,
			label: AppStrings.Integrations.HomeAssistant.Actions.Cover.EntityLabel(),
			required: true),
		ActionParameter.Choice(CommandParameterName,
			[
				new ActionParameterOption
				{
					Value = "open", Label = AppStrings.Integrations.HomeAssistant.Actions.Cover.CommandOpen()
				},
				new ActionParameterOption
				{
					Value = "close", Label = AppStrings.Integrations.HomeAssistant.Actions.Cover.CommandClose()
				},
				new ActionParameterOption
				{
					Value = "stop", Label = AppStrings.Integrations.HomeAssistant.Actions.Cover.CommandStop()
				},
				new ActionParameterOption
				{
					Value = "set_position",
					Label = AppStrings.Integrations.HomeAssistant.Actions.Cover.CommandSetPosition()
				},
				new ActionParameterOption
				{
					Value = "set_tilt_position",
					Label = AppStrings.Integrations.HomeAssistant.Actions.Cover.CommandSetTiltPosition()
				}
			],
			label: AppStrings.Integrations.HomeAssistant.Actions.Cover.CommandLabel(),
			defaultValue: "open",
			required: true),
		ActionParameter.Number(PositionParameterName,
				label: AppStrings.Integrations.HomeAssistant.Actions.Cover.PositionLabel(),
				description: AppStrings.Integrations.HomeAssistant.Actions.Cover.PositionDescription(),
				min: 0,
				max: 100)
			.OnlyWhen(CommandParameterName, "set_position"),
		ActionParameter.Number(TiltPositionParameterName,
				label: AppStrings.Integrations.HomeAssistant.Actions.Cover.TiltPositionLabel(),
				min: 0,
				max: 100)
			.OnlyWhen(CommandParameterName, "set_tilt_position")
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
		=> Task.FromResult(HomeAssistantOptions.Entities(_resolver(), context.Filter, CoverDomain));

	private sealed class Executor : IActionExecutor
	{
		private readonly Func<HomeAssistantConnection?> _resolver;

		public Executor(Func<HomeAssistantConnection?> resolver)
		{
			_resolver = resolver;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (!HomeAssistantServiceCall.TryConnect(_resolver, out var connection, out var rejection))
			{
				return rejection;
			}

			if (!HomeAssistantServiceCall.TryEntity(connection,
				HomeAssistantActionValues.ReadText(context.Parameters, EntityParameterName),
				out var entityId,
				out var rejected))
			{
				return rejected;
			}

			var command = HomeAssistantActionValues.ReadText(context.Parameters, CommandParameterName) ?? "open";
			var data = new Dictionary<string, object?>(StringComparer.Ordinal);

			string service;
			switch (command)
			{
				case "close":
					service = "close_cover";
					break;
				case "stop":
					service = "stop_cover";
					break;
				case "set_position":
					service = "set_cover_position";
					data["position"] =
						HomeAssistantActionValues.ReadNumber(context.Parameters, PositionParameterName, 0, 100) ?? 0d;
					break;
				case "set_tilt_position":
					service = "set_cover_tilt_position";
					data["tilt_position"] =
						HomeAssistantActionValues.ReadNumber(context.Parameters, TiltPositionParameterName, 0, 100) ??
						0d;
					break;
				default:
					service = "open_cover";
					break;
			}

			return await HomeAssistantServiceCall.ExecuteAsync(connection,
				CoverDomain,
				service,
				HomeAssistantServiceCall.Target(entityId),
				data.Count > 0 ? data : null,
				context.CancellationToken);
		}
	}
}
