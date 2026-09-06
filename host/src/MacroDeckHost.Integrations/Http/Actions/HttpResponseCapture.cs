using System.Text.Json;
using MacroDeckHost.Integrations.Http.Client;
using MacroDeckHost.Integrations.Http.JsonPath;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace MacroDeckHost.Integrations.Http.Actions;

internal static class HttpResponseCapture
{
	private const string StatusToken = "$status";
	private const string OkToken = "$ok";
	private const string DurationToken = "$duration";
	private const string BodyToken = "$body";
	private const string TruncatedToken = "$truncated";
	private const string HeadersToken = "$headers";
	private const string HeaderPrefix = "$header:";

	public static async Task ApplyAsync(
		HttpVariableAccessor accessor,
		IReadOnlyDictionary<string, string> captures,
		HttpResponseSnapshot response,
		bool statusExpected,
		ILogger logger)
	{
		foreach (var (name, selector) in captures)
		{
			if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(selector))
			{
				continue;
			}

			if (!TryResolve(selector, response, statusExpected, out var type, out var value))
			{
				logger.Debug("HTTP capture '{Name}' selector '{Selector}' did not resolve; skipped", name, selector);
				continue;
			}

			await HttpVariableWriter.WriteAsync(accessor, name, type, value).ConfigureAwait(false);
		}
	}

	public static async Task ApplyTransportFailureAsync(
		HttpVariableAccessor accessor,
		IReadOnlyDictionary<string, string> captures,
		long durationMs,
		ILogger logger)
	{
		foreach (var (name, selector) in captures)
		{
			if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(selector))
			{
				continue;
			}

			if (!TryResolveTransportFailure(selector, durationMs, out var type, out var value))
			{
				logger.Debug(
					"HTTP capture '{Name}' selector '{Selector}' skipped: no response after a transport failure",
					name,
					selector);
				continue;
			}

			await HttpVariableWriter.WriteAsync(accessor, name, type, value).ConfigureAwait(false);
		}
	}

	private static bool TryResolve(
		string selector,
		HttpResponseSnapshot response,
		bool statusExpected,
		out VariableType type,
		out object value)
	{
		if (TryResolveReserved(selector, response, statusExpected, out type, out value))
		{
			return true;
		}

		// A reserved-token prefix with no matching header is a miss, not a JSON path fall-through.
		if (selector.StartsWith(HeaderPrefix, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		return JsonPathExtractor.TryExtract(response.Body, selector, out type, out value);
	}

	private static bool TryResolveReserved(
		string selector,
		HttpResponseSnapshot response,
		bool statusExpected,
		out VariableType type,
		out object value)
	{
		if (selector.Equals(StatusToken, StringComparison.OrdinalIgnoreCase))
		{
			type = VariableType.Numeric;
			value = (double)response.StatusCode;
			return true;
		}

		if (selector.Equals(OkToken, StringComparison.OrdinalIgnoreCase))
		{
			type = VariableType.Boolean;
			value = statusExpected;
			return true;
		}

		if (selector.Equals(DurationToken, StringComparison.OrdinalIgnoreCase))
		{
			type = VariableType.Numeric;
			value = (double)response.DurationMs;
			return true;
		}

		if (selector.Equals(BodyToken, StringComparison.OrdinalIgnoreCase))
		{
			type = VariableType.Text;
			value = response.Body;
			return true;
		}

		if (selector.Equals(TruncatedToken, StringComparison.OrdinalIgnoreCase))
		{
			type = VariableType.Boolean;
			value = response.BodyTruncated;
			return true;
		}

		if (selector.Equals(HeadersToken, StringComparison.OrdinalIgnoreCase))
		{
			type = VariableType.Text;
			value = JsonSerializer.Serialize(response.Headers);
			return true;
		}

		if (selector.StartsWith(HeaderPrefix, StringComparison.OrdinalIgnoreCase) &&
			response.Headers.TryGetValue(selector[HeaderPrefix.Length..], out var headerValue))
		{
			type = VariableType.Text;
			value = headerValue;
			return true;
		}

		type = VariableType.Text;
		value = string.Empty;
		return false;
	}

	private static bool TryResolveTransportFailure(
		string selector,
		long durationMs,
		out VariableType type,
		out object value)
	{
		if (selector.Equals(StatusToken, StringComparison.OrdinalIgnoreCase))
		{
			type = VariableType.Numeric;
			value = 0d;
			return true;
		}

		if (selector.Equals(OkToken, StringComparison.OrdinalIgnoreCase))
		{
			type = VariableType.Boolean;
			value = false;
			return true;
		}

		if (selector.Equals(DurationToken, StringComparison.OrdinalIgnoreCase))
		{
			type = VariableType.Numeric;
			value = (double)durationMs;
			return true;
		}

		if (selector.Equals(BodyToken, StringComparison.OrdinalIgnoreCase))
		{
			type = VariableType.Text;
			value = string.Empty;
			return true;
		}

		type = VariableType.Text;
		value = string.Empty;
		return false;
	}
}
