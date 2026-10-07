using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Application.AdGuardHome;
using Errors = MacroDeckHost.Localization.AppStrings.Integrations.AdGuardHome.Errors;
using Params = MacroDeckHost.Localization.AppStrings.Integrations.AdGuardHome.Params;

namespace MacroDeckHost.Integrations.AdGuardHome.Actions;

internal interface IAdGuardHomeActionTarget
{
	IReadOnlyList<AdGuardHomeSnapshot> Snapshots { get; }

	Task<AdGuardHomeCommandOutcome> ExecuteAsync(
		string entryId,
		AdGuardHomeCommand command,
		CancellationToken cancellationToken);
}

internal class AdGuardHomeAction : IDynamicOptionsActionDefinition
{
	public const string InstanceParameter = "instance";

	private readonly IAdGuardHomeActionTarget _target;
	private readonly Func<IReadOnlyDictionary<string, object>, AdGuardHomeCommand?> _command;

	public AdGuardHomeAction(
		string id,
		LocalizedText name,
		LocalizedText description,
		IAdGuardHomeActionTarget target,
		Func<IReadOnlyDictionary<string, object>, AdGuardHomeCommand?> command,
		IReadOnlyList<ActionParameter>? parameters = null)
	{
		Id = id;
		Name = name;
		Description = description;
		_target = target;
		_command = command;
		Parameters =
		[
			ActionParameter.DynamicChoice(InstanceParameter, label: Params.Instance(), required: true),
			.. parameters ?? []
		];
	}

	public string Id { get; }

	public LocalizedText Name { get; }

	public LocalizedText Description { get; }

	public IReadOnlyList<ActionParameter> Parameters { get; }

	public IActionExecutor CreateExecutor() => new Executor(this);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
		=> Task.FromResult(new DynamicOptionsResult
		{
			Options =
			[
				.. _target.Snapshots.Select(snapshot => new ActionParameterOption
				{
					Value = snapshot.EntryId,
					Label = snapshot.Title
				})
			],
			CacheSeconds = 0
		});

	protected AdGuardHomeSnapshot? SnapshotFor(IReadOnlyDictionary<string, object?> parameters)
		=> parameters.GetValueOrDefault(InstanceParameter) is string entryId
			? _target.Snapshots.FirstOrDefault(snapshot => snapshot.EntryId == entryId)
			: null;

	public static ActionResult ToResult(AdGuardHomeCommandOutcome outcome)
		=> outcome switch
		{
			AdGuardHomeCommandOutcome.Succeeded => ActionResult.Success(),
			AdGuardHomeCommandOutcome.NotFound => ActionResult.Failed(ActionErrorCodes.NotFound, Errors.InstanceNotFound()),
			AdGuardHomeCommandOutcome.Unauthorized => ActionResult.Failed(ActionErrorCodes.ProviderRejected,
				Errors.Unauthorized()),
			AdGuardHomeCommandOutcome.Timeout => ActionResult.Failed(ActionErrorCodes.Timeout, Errors.Timeout()),
			AdGuardHomeCommandOutcome.Incompatible => ActionResult.Failed(ActionErrorCodes.Unavailable,
				Errors.Incompatible()),
			AdGuardHomeCommandOutcome.Redirected => ActionResult.Failed(ActionErrorCodes.ProviderError,
				Errors.Redirected()),
			AdGuardHomeCommandOutcome.Unreachable => ActionResult.Failed(ActionErrorCodes.NotConnected,
				Errors.Unreachable()),
			_ => ActionResult.Failed(ActionErrorCodes.ProviderError, Errors.CommandFailed())
		};

	private sealed class Executor(AdGuardHomeAction action) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (context.Parameters.GetValueOrDefault(InstanceParameter) is not string { Length: > 0 } entryId)
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter, Errors.SelectInstance());
			}

			if (action._command(context.Parameters) is not { } command)
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter, Errors.InvalidDuration());
			}

			return ToResult(await action._target.ExecuteAsync(entryId, command, context.CancellationToken));
		}
	}
}

internal sealed class AdGuardHomeStateAction : AdGuardHomeAction, IStateProviderActionDefinition
{
	private readonly Func<AdGuardHomeSnapshot, bool?> _read;

	public AdGuardHomeStateAction(
		string id,
		LocalizedText name,
		LocalizedText description,
		IAdGuardHomeActionTarget target,
		Func<IReadOnlyDictionary<string, object>, AdGuardHomeCommand?> command,
		Func<AdGuardHomeSnapshot, bool?> read)
		: base(id, name, description, target, command)
	{
		_read = read;
	}

	public Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		var snapshot = SnapshotFor(parameters);
		var active = snapshot is { IsConnected: true } ? _read(snapshot) : null;
		return Task.FromResult<ActionStateSnapshot?>(ActionStates.Snapshot(ActionStates.Enablement, active));
	}
}
