using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Decks;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Deck.Actions;

internal sealed class CycleFolderActionDefinition : IDynamicOptionsActionDefinition
{
	private const string OrderParameter = "order";
	private const string FoldersParameter = "folders";
	private const string WrapParameter = "wrap";

	private readonly bool _forward;
	private readonly Func<IDeckNavigator?> _navigator;

	public CycleFolderActionDefinition(bool forward, Func<IDeckNavigator?> navigator)
	{
		_forward = forward;
		_navigator = navigator;
	}

	public string Id => _forward ? "next-folder" : "previous-folder";

	public LocalizedText Name => _forward
		? AppStrings.Integrations.Deck.Actions.NextFolderName()
		: AppStrings.Integrations.Deck.Actions.PreviousFolderName();

	public LocalizedText Description => _forward
		? AppStrings.Integrations.Deck.Actions.NextFolderDescription()
		: AppStrings.Integrations.Deck.Actions.PreviousFolderDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Choice(OrderParameter,
			[
				new ActionParameterOption
				{
					Value = FolderCycle.FolderOrder,
					Label = AppStrings.Integrations.Deck.Actions.CycleOrderFolderOrder()
				},
				new ActionParameterOption
				{
					Value = FolderCycle.Alphabetical,
					Label = AppStrings.Integrations.Deck.Actions.CycleOrderAlphabetical()
				},
				new ActionParameterOption
				{
					Value = FolderCycle.Custom,
					Label = AppStrings.Integrations.Deck.Actions.CycleOrderCustom()
				}
			],
			AppStrings.Integrations.Deck.Actions.CycleOrderLabel(),
			AppStrings.Integrations.Deck.Actions.CycleOrderDescription(),
			FolderCycle.FolderOrder),
		ActionParameter.MultiSelect(FoldersParameter,
				null,
				AppStrings.Integrations.Deck.Actions.CycleFoldersLabel(),
				AppStrings.Integrations.Deck.Actions.CycleFoldersDescription())
			.OnlyWhen(OrderParameter, FolderCycle.Custom),
		ActionParameter.Toggle(WrapParameter,
			AppStrings.Integrations.Deck.Actions.WrapLabel(),
			AppStrings.Integrations.Deck.Actions.WrapDescription(),
			true),
		DeckDeviceTarget.Parameter()
	];

	public IActionExecutor CreateExecutor()
	{
		return new Executor(_forward, _navigator);
	}

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		var folders = _navigator()?.GetFolders() ?? [];
		var options = folders
			.Where(folder
				=> context.Filter is null || folder.Label.Contains(context.Filter, StringComparison.OrdinalIgnoreCase))
			.Select(folder => new ActionParameterOption { Value = folder.Id, Label = folder.Label })
			.ToList();

		return Task.FromResult(new DynamicOptionsResult
		{
			Options = options,
			AllowsCustomValue = false,
			CacheSeconds = 2
		});
	}

	private static string ReadOrder(IReadOnlyDictionary<string, object> parameters)
	{
		var order = ReadText(parameters.GetValueOrDefault(OrderParameter))?.Trim();
		return order is FolderCycle.Alphabetical or FolderCycle.Custom ? order : FolderCycle.FolderOrder;
	}

	private static bool ReadWrap(IReadOnlyDictionary<string, object> parameters)
	{
		return parameters.GetValueOrDefault(WrapParameter) switch
		{
			bool value => value,
			JsonElement { ValueKind: JsonValueKind.False } => false,
			string text when bool.TryParse(text.Trim(), out var value) => value,
			_ => true
		};
	}

	private static List<string> ReadFolderIds(IReadOnlyDictionary<string, object> parameters)
	{
		var items = parameters.GetValueOrDefault(FoldersParameter) switch
		{
			null => [],
			string text when text.TrimStart().StartsWith('[') => FromJson(text),
			string text => text.Split(','),
			JsonElement { ValueKind: JsonValueKind.Array } element => element.EnumerateArray()
				.Select(item => ReadText(item)),
			JsonElement element => [ReadText(element)],
			IEnumerable<string> values => values,
			IEnumerable<object?> values => values.Select(ReadText),
			_ => []
		};

		return items
			.Select(item => item?.Trim())
			.Where(item => !string.IsNullOrEmpty(item))
			.Select(item => item!)
			.ToList();
	}

	private static List<string?> FromJson(string text)
	{
		try
		{
			using var document = JsonDocument.Parse(text);
			return document.RootElement.ValueKind == JsonValueKind.Array
				? document.RootElement.EnumerateArray().Select(item => ReadText(item)).ToList()
				: [];
		}
		catch (JsonException)
		{
			return [];
		}
	}

	private static string? ReadText(object? value)
	{
		return value switch
		{
			null => null,
			string text => text,
			JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
			JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
			JsonElement element => element.GetRawText(),
			_ => value.ToString()
		};
	}

	private sealed class Executor : IActionExecutor
	{
		private readonly bool _forward;
		private readonly Func<IDeckNavigator?> _navigator;

		public Executor(bool forward, Func<IDeckNavigator?> navigator)
		{
			_forward = forward;
			_navigator = navigator;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var navigator = _navigator();
			if (navigator is null)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable,
					AppStrings.Integrations.Deck.Errors.NavigationUnavailable());
			}

			var order = ReadOrder(context.Parameters);
			var folders = navigator.GetFolders();
			IReadOnlyList<string>? custom = null;
			if (order == FolderCycle.Custom)
			{
				var picked = ReadFolderIds(context.Parameters);
				if (picked.Count == 0)
				{
					return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
						AppStrings.Integrations.Deck.Errors.NoFolderSelected());
				}

				custom = FolderCycle.CustomSequence(folders, picked);
				if (custom.Count == 0)
				{
					return ActionResult.Failed(ActionErrorCodes.NotFound,
						AppStrings.Integrations.Deck.Errors.FolderNotFound(picked[0]));
				}
			}

			if (!DeckDeviceTarget.TryResolve(navigator, context, out var originClientId, out var failure))
			{
				return failure!;
			}

			var wrap = ReadWrap(context.Parameters);
			var clients = navigator.GetClients();

			if (string.IsNullOrEmpty(originClientId))
			{
				foreach (var client in clients)
				{
					if (Target(client) is { } target)
					{
						await navigator.ChangeFolderAsync(target, client.ClientId, context.CancellationToken);
					}
				}

				return ActionResult.Success();
			}

			var origin = clients.FirstOrDefault(client => client.ClientId == originClientId);
			if (origin is null || Sequence(origin).Count == 0)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable,
					AppStrings.Integrations.Deck.Errors.OpenFolderNotFound());
			}

			if (Target(origin) is { } next)
			{
				await navigator.ChangeFolderAsync(next, origin.ClientId, context.CancellationToken);
			}

			return ActionResult.Success();

			IReadOnlyList<string> Sequence(DeckClient client)
			{
				return custom ?? FolderCycle.ProfileSequence(folders, client.ProfileId, order);
			}

			string? Target(DeckClient client)
			{
				var target = FolderCycle.Step(Sequence(client), client.FolderId, _forward, wrap);
				return target == client.FolderId ? null : target;
			}
		}
	}
}
