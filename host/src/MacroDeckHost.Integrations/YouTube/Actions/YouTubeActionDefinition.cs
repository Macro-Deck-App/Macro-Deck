using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Integrations.YouTube.Auth;
using MacroDeckHost.Integrations.YouTube.Protocol;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.YouTube.Actions;

internal sealed record YouTubeActionScope(
	YouTubeAccountConnection Connection,
	IReadOnlyDictionary<string, object> Parameters,
	CancellationToken CancellationToken)
{
	public IYouTubeApiClient Api => Connection.Api;

	public YouTubePollerOptions Options => Connection.Options;
}

internal sealed class YouTubeActionDefinition : IDynamicOptionsActionDefinition
{
	internal const string AccountParameterName = "account";

	private static readonly ILogger _logger =
		IntegrationLog.For<YouTubeActionDefinition>(YouTubeIntegration.IntegrationId);

	private readonly Func<YouTubeAccountManager> _accounts;
	private readonly Func<YouTubeActionScope, Task<ActionResult>> _execute;

	public YouTubeActionDefinition(
		Func<YouTubeAccountManager> accounts,
		string id,
		LocalizedText name,
		LocalizedText description,
		IReadOnlyList<ActionParameter> parameters,
		Func<YouTubeActionScope, Task<ActionResult>> execute)
	{
		_accounts = accounts;
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

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
		=> Task.FromResult(new DynamicOptionsResult
		{
			Options = context.ParameterName is AccountParameterName ? _accounts().AccountOptions() : [],
			AllowsCustomValue = true,
			CacheSeconds = 30
		});

	private static ActionParameter AccountParameter()
		=> ActionParameter.DynamicChoice(AccountParameterName,
			label: AppStrings.Integrations.YouTube.Actions.AccountLabel(),
			description: AppStrings.Integrations.YouTube.Actions.AccountDescription(),
			placeholder: AppStrings.Integrations.YouTube.Actions.FirstAccountPlaceholder());

	private sealed class Executor : IActionExecutor
	{
		private readonly YouTubeActionDefinition _definition;

		public Executor(YouTubeActionDefinition definition)
		{
			_definition = definition;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var accountId = YouTubeActionValues.ReadText(context.Parameters, AccountParameterName);
			var connection = _definition._accounts().Resolve(accountId);

			if (connection is null)
			{
				_logger.Warning("YouTube action '{Action}' skipped: its channel is not connected", _definition.Id);

				return ActionResult.Failed(ActionErrorCodes.NotConfigured,
					AppStrings.Integrations.YouTube.Errors.AccountNotFound());
			}

			if (connection.Budget.IsExhausted)
			{
				return YouTubeActionErrors.QuotaExhausted();
			}

			try
			{
				return await _definition._execute(new YouTubeActionScope(connection,
					context.Parameters,
					context.CancellationToken));
			}
			catch (YouTubeOAuthRejectedException)
			{
				_logger.Warning("YouTube action '{Action}' skipped: the sign-in of {Channel} expired",
					_definition.Id,
					connection.Account.ChannelId);

				return ActionResult.Failed(ActionErrorCodes.PermissionDenied,
					AppStrings.Integrations.YouTube.Errors.SignInExpired());
			}
			catch (YouTubeApiException ex)
			{
				_logger.Warning(ex, "YouTube rejected the action '{Action}'", _definition.Id);
				return YouTubeActionErrors.From(ex);
			}
			catch (YouTubeTransientException ex)
			{
				_logger.Warning(ex, "YouTube action '{Action}' could not reach YouTube", _definition.Id);

				return ActionResult.Failed(ActionErrorCodes.NotConnected,
					AppStrings.Integrations.YouTube.Errors.Unreachable());
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				_logger.Warning(ex, "YouTube action '{Action}' failed", _definition.Id);

				return ActionResult.Failed(ActionErrorCodes.ProviderError,
					AppStrings.Integrations.YouTube.Errors.RequestRejected());
			}
		}
	}
}
