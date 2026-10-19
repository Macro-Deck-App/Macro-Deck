using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MacroDeckHost.Integrations.DiscordWebhooks.Protocol;

internal enum DiscordSendOutcome
{
	Sent,
	Rejected,
	Unauthorized,
	NotFound,
	RateLimited,
	Failed,
	Unreachable,
	Timeout
}

internal sealed record DiscordSendResult(DiscordSendOutcome Outcome, int? Status = null, string? Reason = null)
{
	public static DiscordSendResult Sent { get; } = new(DiscordSendOutcome.Sent, 204);
}

internal interface IDiscordWebhookSender
{
	Task<DiscordSendResult> SendAsync(Uri webhook, DiscordWebhookPayload payload, CancellationToken cancellationToken);
}

internal sealed class DiscordWebhookSender : IDiscordWebhookSender
{
	public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(5);

	private static readonly Lazy<HttpClient> _shared =
		new(() => IntegrationHttp.CreateClient(TimeSpan.FromSeconds(15)));

	private readonly Func<HttpClient> _http;
	private readonly Func<TimeSpan, CancellationToken, Task> _delay;

	public DiscordWebhookSender()
		: this(() => _shared.Value, Task.Delay)
	{
	}

	internal DiscordWebhookSender(Func<HttpClient> http, Func<TimeSpan, CancellationToken, Task> delay)
	{
		_http = http;
		_delay = delay;
	}

	public async Task<DiscordSendResult> SendAsync(
		Uri webhook,
		DiscordWebhookPayload payload,
		CancellationToken cancellationToken)
	{
		for (var attempt = 0;; attempt++)
		{
			HttpResponseMessage response;
			try
			{
				response = await _http()
					.PostAsJsonAsync(webhook, payload, DiscordWebhookPayload.JsonOptions, cancellationToken)
					.ConfigureAwait(false);
			}
			catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				return new DiscordSendResult(DiscordSendOutcome.Timeout);
			}
			catch (HttpRequestException)
			{
				return new DiscordSendResult(DiscordSendOutcome.Unreachable);
			}

			using (response)
			{
				if (response.IsSuccessStatusCode)
				{
					return DiscordSendResult.Sent with { Status = (int)response.StatusCode };
				}

				var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
				var status = (int)response.StatusCode;

				if (response.StatusCode == HttpStatusCode.TooManyRequests)
				{
					var wait = RetryAfter(response, body);
					if (attempt == 0 && wait is { } delay && delay <= MaxRetryDelay)
					{
						await _delay(delay, cancellationToken).ConfigureAwait(false);
						continue;
					}

					return new DiscordSendResult(DiscordSendOutcome.RateLimited, status);
				}

				return response.StatusCode switch
				{
					HttpStatusCode.BadRequest => new DiscordSendResult(DiscordSendOutcome.Rejected,
						status,
						ReadMessage(body)),
					HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new DiscordSendResult(
						DiscordSendOutcome.Unauthorized,
						status),
					HttpStatusCode.NotFound => new DiscordSendResult(DiscordSendOutcome.NotFound, status),
					_ => new DiscordSendResult(DiscordSendOutcome.Failed, status)
				};
			}
		}
	}

	internal static TimeSpan? RetryAfter(HttpResponseMessage response, string body)
	{
		try
		{
			using var document = JsonDocument.Parse(body);
			if (document.RootElement.ValueKind == JsonValueKind.Object
				&& document.RootElement.TryGetProperty("retry_after", out var retry)
				&& retry.TryGetDouble(out var seconds)
				&& seconds >= 0)
			{
				return TimeSpan.FromSeconds(seconds);
			}
		}
		catch (JsonException)
		{
		}

		if (response.Headers.RetryAfter?.Delta is { } delta)
		{
			return delta;
		}

		return response.Headers.TryGetValues("Retry-After", out var values)
			&& double.TryParse(values.FirstOrDefault(),
				NumberStyles.Float,
				CultureInfo.InvariantCulture,
				out var headerSeconds)
				? TimeSpan.FromSeconds(headerSeconds)
				: null;
	}

	private static string? ReadMessage(string body)
	{
		try
		{
			using var document = JsonDocument.Parse(body);
			var root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object)
			{
				return null;
			}

			var message = root.TryGetProperty("message", out var text) && text.ValueKind == JsonValueKind.String
				? text.GetString()
				: null;
			var detail = root.TryGetProperty("errors", out var errors) ? FirstError(errors, string.Empty) : null;

			return detail is null ? message : $"{message} ({detail})";
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static string? FirstError(JsonElement element, string path)
	{
		if (element.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		foreach (var property in element.EnumerateObject())
		{
			if (property.Name == "_errors" && property.Value.ValueKind == JsonValueKind.Array)
			{
				foreach (var error in property.Value.EnumerateArray())
				{
					if (error.ValueKind == JsonValueKind.Object
						&& error.TryGetProperty("message", out var message)
						&& message.ValueKind == JsonValueKind.String)
					{
						return path.Length == 0 ? message.GetString() : $"{path}: {message.GetString()}";
					}
				}

				continue;
			}

			var nested = FirstError(property.Value, path.Length == 0 ? property.Name : $"{path}.{property.Name}");
			if (nested is not null)
			{
				return nested;
			}
		}

		return null;
	}
}
