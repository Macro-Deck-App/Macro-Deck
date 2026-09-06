using MacroDeckHost.Integrations.Http.Client;
using MacroDeckHost.Integrations.Http.JsonPath;
using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using Serilog;

namespace MacroDeckHost.Integrations.Http.Actions;

internal sealed class GetJsonValueActionDefinition : IActionDefinition
{
	internal const string PathParameter = "path";
	internal const string VariableParameter = "variable";

	private readonly IHttpRequestClient _client;
	private readonly HttpVariableAccessor _variables;

	public GetJsonValueActionDefinition(IHttpRequestClient client, HttpVariableAccessor variables)
	{
		_client = client;
		_variables = variables;
	}

	public string Id => "get-json-value";

	public LocalizedText Name => AppStrings.Integrations.Http.Actions.GetJsonValueName();

	public LocalizedText Description => AppStrings.Integrations.Http.Actions.GetJsonValueDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Url(HttpRequestBuilder.UrlParameter,
			label: AppStrings.Integrations.Http.Params.UrlLabel(),
			required: true,
			autoPrefixHttps: true),
		ActionParameter.KeyValue(HttpRequestBuilder.HeadersParameter,
			label: AppStrings.Integrations.Http.Params.HeadersLabel()),
		.. HttpAuthParameters.Declare(),
		ActionParameter.Text(PathParameter,
			label: AppStrings.Integrations.Http.Actions.GetJsonValuePathLabel(),
			description: AppStrings.Integrations.Http.Actions.GetJsonValuePathDescription(),
			defaultValue: "$"),
		ActionParameter.Text(VariableParameter,
			label: AppStrings.Integrations.Http.Params.SaveToVariableLabel(),
			required: true)
	];

	public IActionExecutor CreateExecutor() => new Executor(_client, _variables);

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<GetJsonValueActionDefinition>(HttpIntegration.IntegrationId);

		private readonly IHttpRequestClient _client;
		private readonly HttpVariableAccessor _variables;

		public Executor(IHttpRequestClient client, HttpVariableAccessor variables)
		{
			_client = client;
			_variables = variables;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var variableName = HttpActionValues.ReadText(context.Parameters, VariableParameter);
			if (variableName is null)
			{
				_logger.Warning("HTTP get-json-value skipped: no target variable configured");
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Http.Errors.NoTargetVariable());
			}

			if (!HttpRequestBuilder.TryBuild(context.Parameters, out var spec, out var buildError))
			{
				_logger.Warning("HTTP get-json-value skipped: {Reason}", buildError);
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter, buildError);
			}

			var outcome = await _client.SendAsync(spec, context.CancellationToken).ConfigureAwait(false);
			if (outcome.Failure is { } failure)
			{
				var (code, message) = HttpFailureMessages.ForFailure(failure);
				return ActionResult.Failed(code, message);
			}

			var response = outcome.Response!;
			if (response.StatusCode is < 200 or >= 300)
			{
				var (code, message) = HttpFailureMessages.ForStatus(response.StatusCode);
				return ActionResult.Failed(code, message);
			}

			var path = HttpActionValues.ReadText(context.Parameters, PathParameter) ?? "$";
			if (!JsonPathExtractor.TryExtract(response.Body, path, out var type, out var value))
			{
				_logger.Warning("HTTP get-json-value: path '{Path}' did not resolve", path);
				return ActionResult.Failed(ActionErrorCodes.NotFound,
					AppStrings.Integrations.Http.Errors.NoValueAtPath());
			}

			var written = await HttpVariableWriter
				.WriteAsync(_variables, variableName, type, value)
				.ConfigureAwait(false);

			return written
				? ActionResult.Success()
				: ActionResult.Failed(ActionErrorCodes.ProviderError,
					AppStrings.Integrations.Http.Errors.VariableSaveFailed());
		}
	}
}
