using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using Serilog;

namespace MacroDeck.Sdk.MusicPlayer.Actions;

/// <summary>
/// A music-player playback action scoped to a single provider. The target instance is selected via
/// the <c>instance</c> dynamic-choice parameter (options supplied by the provider's own instances);
/// the executor resolves the player and runs <see cref="MusicPlayerActions"/>' command. Also
/// implements <see cref="IDynamicOptionsActionDefinition"/> to supply the instance list (and, for
/// item actions, the track/playlist catalog) to the action-builder.
/// </summary>
public class MusicPlayerActionDefinition : IDynamicOptionsActionDefinition
{
	private static readonly ILogger _logger = Log.ForContext<MusicPlayerActionDefinition>();

	private readonly MusicPlayerResolver _resolver;
	private readonly Func<IReadOnlyList<MusicPlayerInstance>> _getInstances;
	private readonly MusicPlayerCommand _command;

	public MusicPlayerActionDefinition(
		MusicPlayerResolver resolver,
		Func<IReadOnlyList<MusicPlayerInstance>> getInstances,
		string id,
		LocalizedText name,
		LocalizedText description,
		IReadOnlyList<ActionParameter> extraParameters,
		MusicPlayerCommand command)
	{
		_resolver = resolver;
		_getInstances = getInstances;
		Id = id;
		Name = name;
		Description = description;
		_command = command;
		Parameters = [InstanceParameter(), .. extraParameters];
	}

	public string Id { get; }
	public LocalizedText Name { get; }
	public LocalizedText Description { get; }
	public IReadOnlyList<ActionParameter> Parameters { get; }

	public virtual IActionExecutor CreateExecutor() => new Executor(_resolver, _command);

	public virtual Task<DynamicOptionsResult> GetDynamicOptionsAsync(DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		if (context.ParameterName != MusicPlayerActions.InstanceParameterName)
		{
			return Task.FromResult(new DynamicOptionsResult { Options = [] });
		}

		var options = InstanceOptions(_getInstances());
		return Task.FromResult(new DynamicOptionsResult { Options = options, CacheSeconds = 2 });
	}

	/// <summary>Resolves the player for an <c>instance</c> value, falling back to the first available.</summary>
	protected IMusicPlayer? Resolve(string? instanceId)
		=> string.IsNullOrEmpty(instanceId) ? _resolver(null) : _resolver(instanceId) ?? _resolver(null);

	/// <summary>Label of the empty instance value, shared with the parameter's placeholder.</summary>
	protected const string FirstAvailableLabel = "First available";

	protected static IReadOnlyList<ActionParameterOption> InstanceOptions(IReadOnlyList<MusicPlayerInstance> instances)
	{
		var options = new List<ActionParameterOption> { new() { Value = "", Label = FirstAvailableLabel } };
		options.AddRange(instances.Select(i => new ActionParameterOption { Value = i.Id, Label = i.DisplayName }));
		return options;
	}

	/// <summary>
	/// The placeholder repeats the label of the empty option <see cref="InstanceOptions"/> ships, so the
	/// field reads the same before the options have loaded as after - an unconfigured instance already
	/// targets the first available player, and an empty-looking field said otherwise.
	/// </summary>
	protected static ActionParameter InstanceParameter()
		=> ActionParameter.DynamicChoice(MusicPlayerActions.InstanceParameterName,
			label: "Player",
			description: "Which music player to control. Leave empty for the first available.",
			placeholder: FirstAvailableLabel);

	private sealed class Executor : IActionExecutor
	{
		private readonly MusicPlayerResolver _resolver;
		private readonly MusicPlayerCommand _command;

		public Executor(MusicPlayerResolver resolver, MusicPlayerCommand command)
		{
			_resolver = resolver;
			_command = command;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var player = ResolveInstance(context.Parameters);
			if (player is null)
			{
				return ActionResult.Failed(ActionErrorCodes.NotConfigured, "No music player is set up.");
			}

			try
			{
				await _command(player, context.Parameters, context.Interactions, context.CancellationToken);
				return ActionResult.Success();
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				_logger.Warning(ex, "Music player action failed");
				return ActionResult.Failed(ActionErrorCodes.ProviderError,
					"The music player could not run this action.");
			}
		}

		private IMusicPlayer? ResolveInstance(IReadOnlyDictionary<string, object> values)
		{
			var instanceId = values.GetValueOrDefault(MusicPlayerActions.InstanceParameterName)?.ToString();
			return string.IsNullOrEmpty(instanceId) ? _resolver(null) : _resolver(instanceId) ?? _resolver(null);
		}
	}
}

/// <summary>
/// A music-player action that can also drive a button's state appearance (e.g. a play/pause toggle
/// showing "Playing" vs. "Paused"). <paramref name="stateMapper"/> is called with <c>null</c> when
/// no player is resolvable or the state read fails, so it alone decides the "unavailable" snapshot -
/// the mapper's states are shown for every instance regardless of whether the current one is reachable.
/// </summary>
public sealed class MusicPlayerStateActionDefinition : MusicPlayerActionDefinition, IStateProviderActionDefinition
{
	private static readonly ILogger _logger = Log.ForContext<MusicPlayerStateActionDefinition>();

	private readonly MusicPlayerResolver _resolver;
	private readonly Func<MusicPlayerState?, ActionStateSnapshot> _stateMapper;
	private readonly MusicPlayerCommand _command;
	private readonly Func<MusicPlayerState, string?>? _expectedStateMapper;

	public MusicPlayerStateActionDefinition(
		MusicPlayerResolver resolver,
		Func<IReadOnlyList<MusicPlayerInstance>> getInstances,
		string id,
		LocalizedText name,
		LocalizedText description,
		IReadOnlyList<ActionParameter> extraParameters,
		MusicPlayerCommand command,
		Func<MusicPlayerState?, ActionStateSnapshot> stateMapper)
		: this(resolver,
			getInstances,
			id,
			name,
			description,
			extraParameters,
			command,
			stateMapper,
			expectedStateMapper: null)
	{
	}

	internal MusicPlayerStateActionDefinition(
		MusicPlayerResolver resolver,
		Func<IReadOnlyList<MusicPlayerInstance>> getInstances,
		string id,
		LocalizedText name,
		LocalizedText description,
		IReadOnlyList<ActionParameter> extraParameters,
		MusicPlayerCommand command,
		Func<MusicPlayerState?, ActionStateSnapshot> stateMapper,
		Func<MusicPlayerState, string?>? expectedStateMapper)
		: base(resolver, getInstances, id, name, description, extraParameters, command)
	{
		_resolver = resolver;
		_stateMapper = stateMapper;
		_command = command;
		_expectedStateMapper = expectedStateMapper;
	}

	public override IActionExecutor CreateExecutor() => _expectedStateMapper is null
		? base.CreateExecutor()
		: new OptimisticExecutor(_resolver, _command, _expectedStateMapper);

	public async Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		var instanceId = parameters.GetValueOrDefault(MusicPlayerActions.InstanceParameterName)?.ToString();
		var player = string.IsNullOrEmpty(instanceId) ? _resolver(null) : _resolver(instanceId) ?? _resolver(null);
		if (player is null)
		{
			return _stateMapper(null);
		}

		try
		{
			var state = await player.GetStateAsync(cancellationToken);
			return _stateMapper(state);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// The host already turns an unhandled exception here into a null snapshot, but null means
			// "nothing is known" - a player that is momentarily unreachable still has a known state
			// set, so mapping the failure to the mapper's own unavailable state is more informative.
			_logger.Warning(ex, "Music player state read failed");
			return _stateMapper(null);
		}
	}

	private sealed class OptimisticExecutor(
		MusicPlayerResolver resolver,
		MusicPlayerCommand command,
		Func<MusicPlayerState, string?> expectedStateMapper) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var instanceId = context.Parameters.GetValueOrDefault(MusicPlayerActions.InstanceParameterName)?.ToString();
			var player = string.IsNullOrEmpty(instanceId) ? resolver(null) : resolver(instanceId) ?? resolver(null);
			if (player is null)
			{
				return ActionResult.Failed(ActionErrorCodes.NotConfigured, "No music player is set up.");
			}

			MusicPlayerState? before = null;
			try
			{
				before = await player.GetStateAsync(context.CancellationToken);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				before = null;
			}

			try
			{
				await command(player, context.Parameters, context.Interactions, context.CancellationToken);
				var expectedStateId = before is null ? null : expectedStateMapper(before);
				return expectedStateId is null ? ActionResult.Success() : ActionResult.Success(expectedStateId);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				_logger.Warning(ex, "Music player action failed");
				return ActionResult.Failed(ActionErrorCodes.ProviderError,
					"The music player could not run this action.");
			}
		}
	}
}

/// <summary>
/// Play a specific track or playlist. The item parameter is optional: when the user left it empty at
/// configure time, the executor asks the triggering client to pick an item at run time via
/// <see cref="IActionInteractions.RequestItemPicker"/>; otherwise it plays the configured item
/// directly. Catalog options for the parameter come from <see cref="IMusicPlayerCatalogProvider"/>.
/// </summary>
public sealed class MusicPlayerItemActionDefinition : MusicPlayerActionDefinition
{
	private static readonly ILogger _logger = Log.ForContext<MusicPlayerItemActionDefinition>();

	private readonly string _integrationId;
	private readonly MusicPlayerResolver _resolver;
	private readonly Func<IReadOnlyList<MusicPlayerInstance>> _getInstances;
	private readonly MusicPlayerCatalogItemKind _kind;
	private readonly string _itemParameterName;

	public MusicPlayerItemActionDefinition(
		string integrationId,
		MusicPlayerResolver resolver,
		Func<IReadOnlyList<MusicPlayerInstance>> getInstances,
		string id,
		LocalizedText name,
		LocalizedText description,
		MusicPlayerCatalogItemKind kind,
		string itemParameterName,
		string itemLabel)
		: base(resolver,
			getInstances,
			id,
			name,
			description,
			[
				ActionParameter.Autocomplete(itemParameterName,
					label: itemLabel,
					description: "Leave empty to pick at run time.",
					placeholder: $"Search {itemLabel.ToLowerInvariant()}…",
					required: false)
			],
			NoOpCommand)
	{
		_integrationId = integrationId;
		_resolver = resolver;
		_getInstances = getInstances;
		_kind = kind;
		_itemParameterName = itemParameterName;
	}

	// The item action has its own executor (needs _kind/_itemParameterName, which the base
	// command delegate can't capture), so the base command is a no-op.
	private static Task NoOpCommand(
		IMusicPlayer _,
		IReadOnlyDictionary<string, object> __,
		IActionInteractions? ___,
		CancellationToken ____)
		=> Task.CompletedTask;

	public override IActionExecutor CreateExecutor() => new ItemExecutor(this, _resolver);

	public override async Task<DynamicOptionsResult> GetDynamicOptionsAsync(DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		if (context.ParameterName == MusicPlayerActions.InstanceParameterName)
		{
			return new DynamicOptionsResult { Options = InstanceOptions(_getInstances()), CacheSeconds = 2 };
		}

		if (context.ParameterName != _itemParameterName)
		{
			return new DynamicOptionsResult { Options = [] };
		}

		var instanceId = ResolveInstanceId(context.CurrentParameters);
		if (instanceId is null)
		{
			return new DynamicOptionsResult { Options = [] };
		}

		if (_resolver(instanceId) is not IMusicPlayerCatalogProvider catalog)
		{
			return new DynamicOptionsResult { Options = [], AllowsCustomValue = true };
		}

		try
		{
			var items = await catalog.GetCatalogAsync(instanceId, _kind, context.Filter, cancellationToken);
			var options = items.Select(i => new ActionParameterOption { Value = i.Id, Label = i.Title }).ToList();
			return new DynamicOptionsResult { Options = options, AllowsCustomValue = true, CacheSeconds = 5 };
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			return new DynamicOptionsResult { Options = [], AllowsCustomValue = true };
		}
		catch (Exception ex)
		{
			// Unlike the runtime picker, degrading to an empty list is right here: this parameter
			// accepts a custom value, so the user can still type the id even with no suggestions. It
			// is logged rather than silent, because "the autocomplete is empty" and "the provider is
			// unreachable" look identical from the editor.
			_logger.Warning(ex, "Could not load {Kind} suggestions for {Instance}", _kind, instanceId);
			return new DynamicOptionsResult { Options = [], AllowsCustomValue = true };
		}
	}

	private async Task<ActionResult> PlayItemAsync(
		ICatalogMusicPlayer player,
		IReadOnlyDictionary<string, object> values,
		IActionInteractions? interactions,
		string? originClientId,
		CancellationToken cancellationToken)
	{
		var itemId = MusicPlayerActions.GetString(values, _itemParameterName);
		var localInstanceId = values.GetValueOrDefault(MusicPlayerActions.InstanceParameterName)?.ToString();

		if (!string.IsNullOrWhiteSpace(itemId))
		{
			var trimmed = itemId.Trim();
			await player.PlayItemAsync(new MusicPlayerCatalogItem(trimmed, trimmed, _kind), cancellationToken);
			return ActionResult.Success();
		}

		// No item configured → ask the triggering client to pick one now (fire-and-forget). The
		// picker needs the globally-unique instance id (the client's reply is routed through the
		// host registry, which keys by integrationId::localId). RequestItemPicker is documented
		// fire-and-forget, so nothing has played yet - Accepted, not Success.
		var globalInstanceId = ToGlobalInstanceId(localInstanceId);
		if (globalInstanceId is null)
		{
			return ActionResult.Failed(ActionErrorCodes.NotFound,
				"No music player instance was available to ask for an item.");
		}

		interactions?.RequestItemPicker(originClientId, globalInstanceId, _kind);
		return ActionResult.Accepted("Waiting for an item to be picked.");
	}

	private string? ToGlobalInstanceId(string? localInstanceId)
	{
		if (!string.IsNullOrEmpty(localInstanceId))
		{
			return $"{_integrationId}::{localInstanceId}";
		}

		var instances = _getInstances();
		return instances.Count > 0 ? $"{_integrationId}::{instances[0].Id}" : null;
	}

	private string? ResolveInstanceId(IReadOnlyDictionary<string, object?> currentParameters)
	{
		var instanceId = currentParameters.GetValueOrDefault(MusicPlayerActions.InstanceParameterName)?.ToString();
		if (!string.IsNullOrEmpty(instanceId))
		{
			return instanceId;
		}

		var instances = _getInstances();
		return instances.Count > 0 ? instances[0].Id : null;
	}

	private sealed class ItemExecutor : IActionExecutor
	{
		private readonly MusicPlayerItemActionDefinition _owner;
		private readonly MusicPlayerResolver _resolver;

		public ItemExecutor(MusicPlayerItemActionDefinition owner, MusicPlayerResolver resolver)
		{
			_owner = owner;
			_resolver = resolver;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var instanceId = context.Parameters.GetValueOrDefault(MusicPlayerActions.InstanceParameterName)?.ToString();
			var player = string.IsNullOrEmpty(instanceId) ? _resolver(null) : _resolver(instanceId) ?? _resolver(null);
			if (player is null)
			{
				return ActionResult.Failed(ActionErrorCodes.NotConfigured, "No music player is set up.");
			}

			if (player is not ICatalogMusicPlayer catalogPlayer)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable,
					"This music player cannot play catalog items.");
			}

			try
			{
				return await _owner.PlayItemAsync(catalogPlayer,
					context.Parameters,
					context.Interactions,
					context.OriginClientId,
					context.CancellationToken);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				_logger.Warning(ex, "Music player item action failed");
				return ActionResult.Failed(ActionErrorCodes.ProviderError,
					"The music player could not run this action.");
			}
		}
	}
}
