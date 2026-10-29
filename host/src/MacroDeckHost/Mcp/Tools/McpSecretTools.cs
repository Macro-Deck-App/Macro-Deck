using System.ComponentModel;
using System.Text.Json.Serialization;
using MacroDeckHost.Application.Mcp;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MacroDeckHost.Mcp.Tools;

public sealed record McpSetSecretResult(McpSecretReferenceValue Secret, string Kind, string Label);

public sealed record McpSecretReferenceValue(
	[property: JsonPropertyName("$secret")]
	string Id);

[McpServerToolType]
public sealed class McpSecretTools
{
	public const string ElicitationUnavailable = "ELICITATION_UNAVAILABLE";
	public const string EntryDeclined = "SECRET_ENTRY_DECLINED";
	public const string EntryExpired = "SECRET_ENTRY_EXPIRED";
	private const string ElicitationKey = "secret";

	private readonly IMcpSecretEntryStore _store;
	private readonly McpTextResolver _text;
	private readonly TimeProvider _timeProvider;
	private readonly IHttpContextAccessor _httpContext;

	public McpSecretTools(
		IMcpSecretEntryStore store,
		McpTextResolver text,
		TimeProvider timeProvider,
		IHttpContextAccessor httpContext)
	{
		_httpContext = httpContext;
		_store = store;
		_text = text;
		_timeProvider = timeProvider;
	}

	[McpServerTool(Name = "set_secret", Title = "Set secret", UseStructuredContent = true)]
	[Description("Asks the user to type a secret (password, API key, token) into a Macro Deck page in their browser "
		+ "and returns a reference to it. The value never passes through this client. Use the returned "
		+ "{\"$secret\":\"<id>\"} object as the value of a parameter marked secret in the catalog.")]
	public async Task<McpSetSecretResult> SetSecret(
		McpServer server,
		RequestContext<CallToolRequestParams> context,
		[Description("password or secret. A password can later be shown to the user in Macro Deck, a secret never.")]
		string kind,
		[Description("A short label the user sees, for example \"OBS WebSocket password\".")]
		string label,
		CancellationToken cancellationToken = default)
	{
		var secretKind = kind?.Trim().ToLowerInvariant() switch
		{
			"password" => SecretKind.Password,
			"secret" => SecretKind.Secret,
			_ => throw McpToolErrors.Create(McpToolErrors.InvalidArgument, "kind must be password or secret.")
		};

		var trimmedLabel = label?.Trim() ?? string.Empty;
		if (trimmedLabel.Length is 0 or > McpSecretEntryStore.MaxLabelLength)
		{
			throw McpToolErrors.Create(McpToolErrors.InvalidArgument,
				$"label must be 1 to {McpSecretEntryStore.MaxLabelLength} characters.");
		}

		if (!Guid.TryParse(context.User?.FindFirst(McpClaimTypes.TokenId)?.Value, out var tokenId)
			|| McpPageUrl.Origin(_httpContext.HttpContext) is not { } origin)
		{
			throw McpToolErrors.Create(ElicitationUnavailable, "The secret page is not available.");
		}

		if (context.Params?.RequestState is { } state && McpElicitation.Answer(context, ElicitationKey) is { } answer)
		{
			return await AwaitEntry(server, state, tokenId, answer, secretKind, trimmedLabel, cancellationToken);
		}

		if (server.ClientCapabilities?.Elicitation?.Url is null)
		{
			throw McpToolErrors.Create(ElicitationUnavailable,
				"This client does not support URL elicitation, so the user cannot enter the secret safely.");
		}

		var clientName = server.ClientInfo?.Name is { Length: > 0 } name
			? name
			: context.User?.FindFirst(McpClaimTypes.TokenName)?.Value ?? string.Empty;
		var entry = _store.Create(tokenId, clientName, secretKind, trimmedLabel);

		throw McpElicitation.Ask(ElicitationKey,
			new ElicitRequestParams
			{
				Mode = "url",
				ElicitationId = entry.Id,
				Url = $"{origin}{McpPaths.SecretEntry}/{entry.Id}",
				Message = await _text.Resolve(AppStrings.Mcp.Elicitation.SetSecret(trimmedLabel)) ?? string.Empty
			},
			entry.Id);
	}

	private async Task<McpSetSecretResult> AwaitEntry(
		McpServer server,
		string entryId,
		Guid tokenId,
		ElicitResult answer,
		SecretKind kind,
		string label,
		CancellationToken cancellationToken)
	{
		// The request state comes back from the client, so it only ever names an entry of the same token.
		var entry = _store.FindForToken(entryId, tokenId);
		if (entry is null)
		{
			throw McpToolErrors.Create(EntryExpired, "The secret request is no longer valid.");
		}

		try
		{
			if (!answer.IsAccepted)
			{
				throw McpToolErrors.Create(EntryDeclined, "The user did not open the secret page.");
			}

			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			var remaining = entry.ExpiresAt - _timeProvider.GetUtcNow().UtcDateTime;
			timeout.CancelAfter(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
			var secretId = await entry.Completion.WaitAsync(timeout.Token);

			if (server.SessionId is not null)
			{
				await server.SendNotificationAsync(NotificationMethods.ElicitationCompleteNotification,
					new ElicitationCompleteNotificationParams { ElicitationId = entry.Id },
					cancellationToken: cancellationToken);
			}

			return new McpSetSecretResult(new McpSecretReferenceValue(secretId.ToString()),
				kind == SecretKind.Password ? "password" : "secret",
				label);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw McpToolErrors.Create(EntryExpired, "The user did not enter the secret in time.");
		}
		finally
		{
			_store.Abandon(entry.Id);
		}
	}
}
