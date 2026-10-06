using System.Net;
using System.Text.Json;

namespace MacroDeckHost.Integrations.YouTube.Protocol;

internal sealed class YouTubeApiException : Exception
{
	public YouTubeApiException()
	{
	}

	public YouTubeApiException(string message)
		: base(message)
	{
	}

	public YouTubeApiException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	public YouTubeApiException(HttpStatusCode status, string? reason, string message)
		: base(message)
	{
		Status = status;
		Reason = reason;
	}

	public HttpStatusCode Status { get; }

	public string? Reason { get; }

	public bool IsQuotaExceeded => Reason is "quotaExceeded" or "dailyLimitExceeded";

	public bool IsRateLimited
		=> Reason is "rateLimitExceeded" or "userRequestsExceedRateLimit" || Status is HttpStatusCode.TooManyRequests;

	public bool IsUnauthorized => Status is HttpStatusCode.Unauthorized;

	public static YouTubeApiException FromResponse(HttpStatusCode status, string? body)
	{
		var (reason, message) = ReadError(body);

		return new YouTubeApiException(status,
			reason,
			message is { Length: > 0 }
				? $"YouTube answered {(int)status} {reason}: {message}"
				: $"YouTube answered {(int)status} {reason}");
	}

	private static (string? Reason, string? Message) ReadError(string? body)
	{
		if (string.IsNullOrWhiteSpace(body))
		{
			return (null, null);
		}

		try
		{
			using var document = JsonDocument.Parse(body);
			var root = document.RootElement;

			if (root.ValueKind is not JsonValueKind.Object ||
				!root.TryGetProperty("error", out var error) ||
				error.ValueKind is not JsonValueKind.Object)
			{
				return (null, null);
			}

			string? reason = null;
			if (error.TryGetProperty("errors", out var errors) && errors.ValueKind is JsonValueKind.Array)
			{
				reason = errors.EnumerateArray()
					.Select(item => ReadString(item, "reason"))
					.FirstOrDefault(value => value is { Length: > 0 });
			}

			return (reason, ReadString(error, "message"));
		}
		catch (JsonException)
		{
			return (null, null);
		}
	}

	private static string? ReadString(JsonElement element, string name)
		=> element.ValueKind is JsonValueKind.Object &&
			element.TryGetProperty(name, out var value) &&
			value.ValueKind is JsonValueKind.String
				? value.GetString()
				: null;
}

internal sealed class YouTubeTransientException : Exception
{
	public YouTubeTransientException()
	{
	}

	public YouTubeTransientException(string message)
		: base(message)
	{
	}

	public YouTubeTransientException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
