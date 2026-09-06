using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using MacroDeck.Localization;
using MacroDeckHost.Integrations.Http.Client;
using MacroDeckHost.Localization;
using MacroDeck.Sdk.Logging;
using Serilog;

namespace MacroDeckHost.Integrations.Http.Actions;

internal static class HttpRequestBuilder
{
	internal const string MethodParameter = "method";
	internal const string UrlParameter = "url";
	internal const string QueryParameter = "query";
	internal const string HeadersParameter = "headers";
	internal const string BodyTypeParameter = "bodyType";
	internal const string JsonBodyParameter = "jsonBody";
	internal const string FormBodyParameter = "formBody";
	internal const string TextBodyParameter = "textBody";
	internal const string FilePathParameter = "filePath";
	internal const string FileFieldNameParameter = "fileFieldName";
	internal const string ContentTypeParameter = "contentType";
	internal const string ExpectedStatusParameter = "expectedStatus";
	internal const string CapturesParameter = "captures";
	internal const string TimeoutParameter = "timeout";
	internal const string FollowRedirectsParameter = "followRedirects";
	internal const string ValidateTlsParameter = "validateTls";
	internal const string MaxResponseBytesParameter = "maxResponseBytes";

	private const string BodyNone = "none";
	private const string BodyJson = "json";
	private const string BodyForm = "form";
	private const string BodyText = "text";
	private const string BodyMultipart = "multipart";

	private const double DefaultTimeoutMs = 30_000;
	private const double MinTimeoutMs = 1_000;
	private const double MaxTimeoutMs = 300_000;

	private const long DefaultMaxResponseBytes = 262_144;
	private const long MinResponseBytes = 1_024;
	private const long MaxResponseBytesCap = 10 * 1024 * 1024;

	private const string DefaultFileFieldName = "file";

	private static readonly Regex _methodPattern =
		new("^[A-Za-z][A-Za-z0-9!#$%&'*+.^_`|~-]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly ILogger _logger =
		IntegrationLog.For(HttpIntegration.IntegrationId, typeof(HttpRequestBuilder));

	public static bool TryBuild(
		IReadOnlyDictionary<string, object> parameters,
		out HttpRequestSpec spec,
		out LocalizedText errorMessage)
	{
		spec = null!;
		errorMessage = default;

		var url = HttpActionValues.ReadText(parameters, UrlParameter);
		if (url is null)
		{
			errorMessage = AppStrings.Integrations.Http.Errors.NoUrlConfigured();
			return false;
		}

		var query = HttpActionValues.ReadKeyValue(parameters, QueryParameter);
		if (!HttpUrlBuilder.TryBuild(url, query, out var uri))
		{
			errorMessage = AppStrings.Integrations.Http.Errors.UrlMustBeAbsolute();
			return false;
		}

		var method = HttpActionValues.ReadMethod(parameters, MethodParameter);
		if (!_methodPattern.IsMatch(method))
		{
			errorMessage = AppStrings.Integrations.Http.Errors.InvalidMethod();
			return false;
		}

		if (!HttpAuthParameters.TryRead(parameters, out var auth, out var authError))
		{
			errorMessage = authError;
			return false;
		}

		var headers = HttpActionValues.ReadKeyValue(parameters, HeadersParameter);

		if (!TryBuildBody(parameters, out var body, out var bodyError))
		{
			errorMessage = bodyError;
			return false;
		}

		var rawTimeoutMs = HttpActionValues.ReadDurationMilliseconds(parameters, TimeoutParameter, DefaultTimeoutMs);
		var timeoutMs = Math.Clamp(double.IsFinite(rawTimeoutMs) ? rawTimeoutMs : DefaultTimeoutMs,
			MinTimeoutMs,
			MaxTimeoutMs);

		var rawMaxBytes = HttpActionValues.ReadNumber(parameters, MaxResponseBytesParameter, DefaultMaxResponseBytes);
		var maxResponseBytes = Math.Clamp(double.IsFinite(rawMaxBytes) ? (long)rawMaxBytes : DefaultMaxResponseBytes,
			MinResponseBytes,
			MaxResponseBytesCap);

		var followRedirects = HttpActionValues.ReadBool(parameters, FollowRedirectsParameter, true);
		if (body is HttpBodyMultipart && followRedirects)
		{
			// .NET cannot rewind a StreamContent that has already been read to re-send the body on a
			// 307/308 redirect, so a multipart request never follows redirects.
			_logger.Information("HTTP request to {Host} forced followRedirects off because the body is multipart",
				uri.Host);
			followRedirects = false;
		}

		var validateTls = HttpActionValues.ReadBool(parameters, ValidateTlsParameter, true);

		spec = new HttpRequestSpec(method,
			uri,
			headers,
			auth,
			body,
			TimeSpan.FromMilliseconds(timeoutMs),
			followRedirects,
			validateTls,
			maxResponseBytes);
		return true;
	}

	private static bool TryBuildBody(
		IReadOnlyDictionary<string, object> parameters,
		out HttpBody body,
		out LocalizedText errorMessage)
	{
		var bodyType = HttpActionValues.ReadText(parameters, BodyTypeParameter) ?? BodyNone;
		var contentType = HttpActionValues.ReadText(parameters, ContentTypeParameter);

		if (contentType is not null && !MediaTypeHeaderValue.TryParse(contentType, out _))
		{
			body = HttpBody.None;
			errorMessage = AppStrings.Integrations.Http.Errors.InvalidContentType();
			return false;
		}

		switch (bodyType.ToLowerInvariant())
		{
			case BodyJson:
			{
				var json = HttpActionValues.ReadRawText(parameters, JsonBodyParameter) ?? "null";
				if (!IsValidJson(json))
				{
					body = HttpBody.None;
					errorMessage = AppStrings.Integrations.Http.Errors.InvalidJsonBody();
					return false;
				}

				body = new HttpBodyJson(json, contentType);
				errorMessage = default;
				return true;
			}

			case BodyForm:
				body = new HttpBodyForm(HttpActionValues.ReadKeyValue(parameters, FormBodyParameter), contentType);
				errorMessage = default;
				return true;

			case BodyText:
				body = new HttpBodyText(HttpActionValues.ReadRawText(parameters, TextBodyParameter) ?? string.Empty,
					contentType);
				errorMessage = default;
				return true;

			case BodyMultipart:
			{
				var filePath = HttpActionValues.ReadText(parameters, FilePathParameter);
				if (filePath is null)
				{
					body = HttpBody.None;
					errorMessage = AppStrings.Integrations.Http.Errors.MultipartNeedsFile();
					return false;
				}

				var fileFieldName = HttpActionValues.ReadText(parameters, FileFieldNameParameter) ??
					DefaultFileFieldName;
				body = new HttpBodyMultipart(HttpActionValues.ReadKeyValue(parameters, FormBodyParameter),
					filePath,
					fileFieldName);
				errorMessage = default;
				return true;
			}

			default:
				body = HttpBody.None;
				errorMessage = default;
				return true;
		}
	}

	private static bool IsValidJson(string json)
	{
		try
		{
			using var document = JsonDocument.Parse(json);
			return true;
		}
		catch (JsonException)
		{
			return false;
		}
	}
}
