using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MacroDeck.Sdk.Logging;
using Serilog;

namespace MacroDeckHost.Integrations.Discord.Webhooks;

internal interface IDiscordWebhookClient
{
	Task<string?> ExecuteAsync(string webhookUrl, DiscordWebhookRequest request, CancellationToken cancellationToken);
}

internal sealed class DiscordWebhookClient : IDiscordWebhookClient
{
	private static readonly ILogger _logger =
		IntegrationLog.For<DiscordWebhookClient>(DiscordIntegration.IntegrationId);

	private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

	private static readonly JsonSerializerOptions _json = new()
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	private static readonly string[] _allowedHosts =
	[
		"discord.com",
		"discordapp.com",
		"ptb.discord.com",
		"canary.discord.com"
	];

	public async Task<string?> ExecuteAsync(
		string webhookUrl,
		DiscordWebhookRequest request,
		CancellationToken cancellationToken)
	{
		var validationError = Validate(webhookUrl, request);
		if (validationError is not null)
		{
			return validationError;
		}

		try
		{
			using var response = await _http
				.PostAsJsonAsync(webhookUrl, request, _json, cancellationToken)
				.ConfigureAwait(false);

			if (response.IsSuccessStatusCode)
			{
				return null;
			}

			return await DescribeFailureAsync(response, cancellationToken).ConfigureAwait(false);
		}
		catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return "Discord did not answer in time.";
		}
		catch (HttpRequestException ex)
		{
			_logger.Warning(ex, "Discord webhook request failed");
			return "Could not reach Discord.";
		}
	}

	internal static string? Validate(string webhookUrl, DiscordWebhookRequest request)
	{
		if (string.IsNullOrWhiteSpace(webhookUrl))
		{
			return "No webhook URL was configured.";
		}

		if (!Uri.TryCreate(webhookUrl, UriKind.Absolute, out var uri) ||
			!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
		{
			return "The webhook URL must be an https:// address.";
		}

		if (!_allowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
		{
			return "That is not a Discord webhook URL. Copy it from a channel's Integrations settings.";
		}

		return request.IsEmpty
			? "The message is empty. Enter message text or fill in at least one embed field."
			: null;
	}

	private static async Task<string> DescribeFailureAsync(
		HttpResponseMessage response,
		CancellationToken cancellationToken)
	{
		var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		_logger.Warning("Discord webhook returned {Status}: {Body}", (int)response.StatusCode, body);

		return response.StatusCode switch
		{
			HttpStatusCode.NotFound => "The webhook no longer exists. It may have been deleted in Discord.",
			HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Discord rejected the webhook token.",
			HttpStatusCode.TooManyRequests => "Discord is rate limiting this webhook. Send messages less often.",
			HttpStatusCode.BadRequest => $"Discord rejected the message: {ReadFirstError(body)}",
			_ => string.Create(CultureInfo.InvariantCulture,
				$"Discord answered with status {(int)response.StatusCode}.")
		};
	}

	private static string ReadFirstError(string body)
	{
		try
		{
			using var document = JsonDocument.Parse(body);
			var root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object)
			{
				return "invalid request";
			}

			return DiscordStateMapper.ReadString(root, "message") ?? "invalid request";
		}
		catch (JsonException)
		{
			return "invalid request";
		}
	}
}
