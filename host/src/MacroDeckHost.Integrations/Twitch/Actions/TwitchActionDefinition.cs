using MacroDeckHost.Integrations.Twitch.Protocol;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Twitch.Actions;

internal sealed record TwitchActionScope(
	TwitchAccountConnection Connection,
	IReadOnlyDictionary<string, object> Parameters,
	IVariableApi? Variables,
	CancellationToken CancellationToken)
{
	public ITwitchHelixClient Helix => Connection.Helix;

	public string UserId => Connection.Account.UserId;
}

internal class TwitchActionDefinition : IDynamicOptionsActionDefinition
{
	internal const string AccountParameterName = "account";

	private static readonly ILogger _logger =
		IntegrationLog.For<TwitchActionDefinition>(TwitchIntegration.IntegrationId);

	private readonly Func<TwitchAccountManager> _accounts;
	private readonly Func<IVariableApi?> _variables;
	private readonly Func<TwitchActionScope, Task<ActionResult>> _execute;

	public TwitchActionDefinition(
		Func<TwitchAccountManager> accounts,
		Func<IVariableApi?> variables,
		string id,
		LocalizedText name,
		LocalizedText description,
		IReadOnlyList<ActionParameter> parameters,
		Func<TwitchActionScope, Task> execute)
		: this(accounts,
			variables,
			id,
			name,
			description,
			parameters,
			async scope =>
			{
				await execute(scope);
				return ActionResult.Success();
			})
	{
	}

	public TwitchActionDefinition(
		Func<TwitchAccountManager> accounts,
		Func<IVariableApi?> variables,
		string id,
		LocalizedText name,
		LocalizedText description,
		IReadOnlyList<ActionParameter> parameters,
		Func<TwitchActionScope, Task<ActionResult>> execute)
	{
		_accounts = accounts;
		_variables = variables;
		Id = id;
		Name = name;
		Description = description;
		Parameters = [AccountParameter(), .. parameters];
		_execute = execute;
	}

	public string Id { get; }

	public LocalizedText Name { get; }

	public LocalizedText Description { get; }

	public IReadOnlyList<ActionParameter> Parameters { get; }

	public IActionExecutor CreateExecutor() => new Executor(this);

	/// <summary>The account this instance's configuration names, or the first one when it names none.</summary>
	protected TwitchAccountConnection? ResolveAccount(object? accountId)
		=> _accounts().Resolve(accountId as string);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		var accounts = _accounts();
		var options = context.ParameterName switch
		{
			AccountParameterName => accounts.AccountOptions(),
			"rewardId" => RewardOptions(accounts, context.CurrentParameters.GetValueOrDefault(AccountParameterName)),
			_ => []
		};

		return Task.FromResult(new DynamicOptionsResult
		{
			Options = options,
			AllowsCustomValue = true,
			CacheSeconds = 30
		});
	}

	private static ActionParameter AccountParameter()
		=> ActionParameter.DynamicChoice(AccountParameterName,
			label: AppStrings.Integrations.Twitch.Actions.AccountLabel(),
			description: AppStrings.Integrations.Twitch.Actions.AccountDescription(),
			placeholder: AppStrings.Integrations.Twitch.Actions.FirstAccountPlaceholder());

	private static IReadOnlyList<ActionParameterOption> RewardOptions(TwitchAccountManager accounts, object? accountId)
	{
		var connection = accounts.Resolve(accountId as string);

		return connection is null
			? []
			:
			[
				.. connection.Rewards.Select(reward => new ActionParameterOption
				{
					Value = reward.Id,
					Label = reward.Title
				})
			];
	}

	private sealed class Executor : IActionExecutor
	{
		private readonly TwitchActionDefinition _definition;

		public Executor(TwitchActionDefinition definition)
		{
			_definition = definition;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var accountId = TwitchActionValues.ReadText(context.Parameters, AccountParameterName);
			var connection = _definition._accounts().Resolve(accountId);

			if (connection is null)
			{
				_logger.Warning("Twitch action '{Action}' skipped: no account {Account}",
					_definition.Id,
					accountId ?? "configured");

				return ActionResult.Failed("ACCOUNT_NOT_FOUND",
					AppStrings.Integrations.Twitch.Errors.AccountNotFound());
			}

			try
			{
				return await _definition._execute(new TwitchActionScope(connection,
					context.Parameters,
					_definition._variables(),
					context.CancellationToken));
			}
			catch (TwitchScopeException)
			{
				_logger.Warning("Twitch action '{Action}' skipped: {Account} did not grant the permission for it",
					_definition.Id,
					connection.Account.Login);

				return ActionResult.Failed(ActionErrorCodes.PermissionDenied,
					AppStrings.Integrations.Twitch.Errors.MissingScopePermission());
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				_logger.Warning(ex, "Twitch action '{Action}' failed", _definition.Id);

				return ActionResult.Failed(ActionErrorCodes.ProviderError,
					AppStrings.Integrations.Twitch.Errors.RequestRejected());
			}
		}
	}
}
