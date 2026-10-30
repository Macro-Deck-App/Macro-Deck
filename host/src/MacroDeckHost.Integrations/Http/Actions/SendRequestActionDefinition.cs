using MacroDeckHost.Integrations.Http.Protocol;
using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace MacroDeckHost.Integrations.Http.Actions;

internal sealed class SendRequestActionDefinition : IActionDefinition
{
	private readonly IHttpRequestClient _client;
	private readonly HttpVariableAccessor _variables;

	public SendRequestActionDefinition(IHttpRequestClient client, HttpVariableAccessor variables)
	{
		_client = client;
		_variables = variables;
	}

	public string Id => "send-request";

	public ActionRiskCategory RiskCategory => ActionRiskCategory.Network;

	public LocalizedText Name => AppStrings.Integrations.Http.Actions.SendRequestName();

	public LocalizedText Description => AppStrings.Integrations.Http.Actions.SendRequestDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Choice(HttpRequestBuilder.MethodParameter,
			[
				new ActionParameterOption { Value = "GET" },
				new ActionParameterOption { Value = "POST" },
				new ActionParameterOption { Value = "PUT" },
				new ActionParameterOption { Value = "PATCH" },
				new ActionParameterOption { Value = "DELETE" },
				new ActionParameterOption { Value = "HEAD" },
				new ActionParameterOption { Value = "OPTIONS" },
				new ActionParameterOption { Value = "QUERY" }
			],
			AppStrings.Integrations.Http.Actions.SendRequestMethodLabel(),
			defaultValue: "GET"),
		ActionParameter.Url(HttpRequestBuilder.UrlParameter,
			AppStrings.Integrations.Http.Params.UrlLabel(),
			required: true,
			autoPrefixHttps: true),
		ActionParameter.KeyValue(HttpRequestBuilder.QueryParameter,
			AppStrings.Integrations.Http.Actions.SendRequestQueryLabel(),
			AppStrings.Integrations.Http.Actions.SendRequestQueryDescription()),
		ActionParameter.KeyValue(HttpRequestBuilder.HeadersParameter,
			AppStrings.Integrations.Http.Params.HeadersLabel(),
			AppStrings.Integrations.Http.Actions.SendRequestHeadersDescription()),
		.. HttpAuthParameters.Declare(),
		ActionParameter.Choice(HttpRequestBuilder.BodyTypeParameter,
			[
				new ActionParameterOption { Value = "none" },
				new ActionParameterOption { Value = "json" },
				new ActionParameterOption { Value = "form" },
				new ActionParameterOption { Value = "text" },
				new ActionParameterOption { Value = "multipart" }
			],
			AppStrings.Integrations.Http.Actions.SendRequestBodyTypeLabel(),
			defaultValue: "none"),
		ActionParameter.Json(HttpRequestBuilder.JsonBodyParameter,
				AppStrings.Integrations.Http.Actions.SendRequestJsonBodyLabel())
			.OnlyWhen(HttpRequestBuilder.BodyTypeParameter, "json"),
		ActionParameter.KeyValue(HttpRequestBuilder.FormBodyParameter,
				AppStrings.Integrations.Http.Actions.SendRequestFormFieldsLabel(),
				AppStrings.Integrations.Http.Actions.SendRequestFormFieldsDescription())
			.OnlyWhen(HttpRequestBuilder.BodyTypeParameter, "form", "multipart"),
		ActionParameter.MultilineText(HttpRequestBuilder.TextBodyParameter,
				AppStrings.Integrations.Http.Actions.SendRequestTextBodyLabel())
			.OnlyWhen(HttpRequestBuilder.BodyTypeParameter, "text"),
		ActionParameter.File(HttpRequestBuilder.FilePathParameter,
				AppStrings.Integrations.Http.Actions.SendRequestFileToUploadLabel())
			.OnlyWhen(HttpRequestBuilder.BodyTypeParameter, "multipart"),
		ActionParameter.Text(HttpRequestBuilder.FileFieldNameParameter,
				AppStrings.Integrations.Http.Actions.SendRequestFileFieldNameLabel(),
				defaultValue: "file")
			.OnlyWhen(HttpRequestBuilder.BodyTypeParameter, "multipart"),
		ActionParameter.Text(HttpRequestBuilder.ContentTypeParameter,
				AppStrings.Integrations.Http.Actions.SendRequestContentTypeLabel(),
				AppStrings.Integrations.Http.Actions.SendRequestContentTypeDescription())
			.OnlyWhen(HttpRequestBuilder.BodyTypeParameter, "json", "form", "text"),
		ActionParameter.Text(HttpRequestBuilder.ExpectedStatusParameter,
			AppStrings.Integrations.Http.Actions.SendRequestExpectedStatusLabel(),
			AppStrings.Integrations.Http.Actions.SendRequestExpectedStatusDescription(),
			defaultValue: "2xx"),
		ActionParameter.KeyValue(HttpRequestBuilder.CapturesParameter,
			AppStrings.Integrations.Http.Actions.SendRequestCapturesLabel(),
			AppStrings.Integrations.Http.Actions.SendRequestCapturesDescription()),
		ActionParameter.Duration(HttpRequestBuilder.TimeoutParameter,
			AppStrings.Integrations.Http.Actions.SendRequestTimeoutLabel(),
			min: 1_000,
			max: 300_000,
			defaultMilliseconds: 30_000),
		ActionParameter.Toggle(HttpRequestBuilder.FollowRedirectsParameter,
			AppStrings.Integrations.Http.Actions.SendRequestFollowRedirectsLabel(),
			defaultValue: true),
		ActionParameter.Toggle(HttpRequestBuilder.ValidateTlsParameter,
			AppStrings.Integrations.Http.Actions.SendRequestValidateTlsLabel(),
			AppStrings.Integrations.Http.Actions.SendRequestValidateTlsDescription(),
			true),
		ActionParameter.Number(HttpRequestBuilder.MaxResponseBytesParameter,
			AppStrings.Integrations.Http.Actions.SendRequestMaxResponseSizeLabel(),
			min: 1_024,
			max: 10_485_760,
			defaultValue: 262_144)
	];

	public IActionExecutor CreateExecutor()
	{
		return new Executor(_client, _variables);
	}

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<SendRequestActionDefinition>(HttpIntegration.IntegrationId);

		private readonly IHttpRequestClient _client;
		private readonly HttpVariableAccessor _variables;

		public Executor(IHttpRequestClient client, HttpVariableAccessor variables)
		{
			_client = client;
			_variables = variables;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (!HttpRequestBuilder.TryBuild(context.Parameters, out var spec, out var buildError))
			{
				_logger.Warning("HTTP send-request skipped: {Reason}", buildError);
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter, buildError);
			}

			var expectedStatusText =
				HttpActionValues.ReadText(context.Parameters, HttpRequestBuilder.ExpectedStatusParameter);
			if (!HttpStatusExpectation.TryParse(expectedStatusText, out var expectedStatus))
			{
				_logger.Warning("HTTP send-request skipped: invalid expected status '{ExpectedStatus}'",
					expectedStatusText);
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Http.Errors.InvalidExpectedStatus());
			}

			var captures = HttpActionValues.ReadKeyValue(context.Parameters, HttpRequestBuilder.CapturesParameter);

			var outcome = await _client.SendAsync(spec, context.CancellationToken).ConfigureAwait(false);
			if (outcome.Failure is { } failure)
			{
				await HttpResponseCapture
					.ApplyTransportFailureAsync(_variables,
						captures,
						outcome.DurationMs,
						_logger,
						UserVariableContext.From(context))
					.ConfigureAwait(false);

				var (code, message) = HttpFailureMessages.ForFailure(failure);
				return ActionResult.Failed(code, message);
			}

			var response = outcome.Response!;
			var statusExpected = expectedStatus.Matches(response.StatusCode);

			await HttpResponseCapture
				.ApplyAsync(_variables, captures, response, statusExpected, _logger, UserVariableContext.From(context))
				.ConfigureAwait(false);

			if (!statusExpected)
			{
				var (code, message) = HttpFailureMessages.ForStatus(response.StatusCode);
				return ActionResult.Failed(code, message);
			}

			return ActionResult.Success();
		}
	}
}
