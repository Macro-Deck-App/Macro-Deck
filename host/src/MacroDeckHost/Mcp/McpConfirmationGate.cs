using System.Text.Json;
using MacroDeckHost.Application.Mcp;
using MacroDeckHost.Localization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MacroDeckHost.Mcp;

public sealed class McpConfirmationGate
{
	public static readonly TimeSpan InAppWait = TimeSpan.FromSeconds(45);

	private const string PageState = "page:";

	private readonly McpConfirmationText _text;
	private readonly McpTextResolver _resolver;
	private readonly IMcpConfirmationStore _store;
	private readonly TimeProvider _timeProvider;
	private readonly IHttpContextAccessor _httpContext;

	public McpConfirmationGate(
		McpConfirmationText text,
		McpTextResolver resolver,
		IMcpConfirmationStore store,
		TimeProvider timeProvider,
		IHttpContextAccessor httpContext)
	{
		_httpContext = httpContext;
		_text = text;
		_resolver = resolver;
		_store = store;
		_timeProvider = timeProvider;
	}

	public async Task Require(
		McpServer server,
		RequestContext<CallToolRequestParams> context,
		IReadOnlyList<string> codes,
		IReadOnlyList<string> lines,
		IReadOnlyList<McpActionRef> actions,
		CancellationToken cancellationToken)
	{
		if (codes.Count == 0)
		{
			return;
		}

		var fingerprint = McpConsent.Fingerprint(context);
		if (context.Params?.RequestState is { } state && McpElicitation.Answer(context, McpConsent.Key) is { } answer)
		{
			if (state == fingerprint)
			{
				if (answer.IsAccepted
					&& answer.Content?.TryGetValue(McpConsent.Key, out var confirmed) == true
					&& confirmed.ValueKind == JsonValueKind.True)
				{
					return;
				}

				throw McpToolErrors.Create(McpConsent.Declined, "The user did not confirm.");
			}

			if (state.StartsWith(PageState, StringComparison.Ordinal))
			{
				await AwaitPage(server, context, state[PageState.Length..], fingerprint, answer, cancellationToken);
				return;
			}
		}

		var elicitation = server.ClientCapabilities?.Elicitation;
		if (elicitation is not null && (elicitation.Form is not null || elicitation.Url is null))
		{
			throw McpElicitation.Ask(McpConsent.Key,
				new ElicitRequestParams
				{
					Mode = "form",
					Message = await _text.Message(lines),
					RequestedSchema = new ElicitRequestParams.RequestSchema
					{
						Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
						{
							[McpConsent.Key] = new ElicitRequestParams.BooleanSchema
								{ Title = await _text.Title(), Default = false }
						},
						Required = [McpConsent.Key]
					}
				},
				fingerprint);
		}

		if (elicitation?.Url is null || McpPageUrl.Origin(_httpContext.HttpContext) is not { } origin)
		{
			await AskInApp(server, context, codes, lines, actions, fingerprint, cancellationToken);
			return;
		}

		var entry = _store.Create(McpConsent.TokenId(context), ClientName(server, context), fingerprint, lines);
		throw McpElicitation.Ask(McpConsent.Key,
			new ElicitRequestParams
			{
				Mode = "url",
				ElicitationId = entry.Id,
				Url = $"{origin}{McpPaths.Confirmation}/{entry.Id}",
				Message = await _resolver.Resolve(AppStrings.Mcp.Elicitation.ConfirmOnPage(string.Join("; ", lines)))
					?? string.Empty
			},
			PageState + entry.Id);
	}

	private async Task AskInApp(
		McpServer server,
		RequestContext<CallToolRequestParams> context,
		IReadOnlyList<string> codes,
		IReadOnlyList<string> lines,
		IReadOnlyList<McpActionRef> actions,
		string fingerprint,
		CancellationToken cancellationToken)
	{
		var entry = _store.GetOrCreateInApp(McpConsent.TokenId(context),
			ClientName(server, context),
			fingerprint,
			lines,
			actions.Distinct().ToList(),
			codes.All(code => code.StartsWith(McpConfirmationKinds.RiskyAction + ":", StringComparison.Ordinal)));

		var remaining = entry.ExpiresAt - _timeProvider.GetUtcNow().UtcDateTime;
		var wait = remaining < InAppWait ? remaining : InAppWait;
		using var timeout = new CancellationTokenSource(wait > TimeSpan.Zero ? wait : TimeSpan.Zero, _timeProvider);
		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
		try
		{
			await entry.Completion.WaitAsync(linked.Token);
		}
		catch (OperationCanceledException)
		{
			cancellationToken.ThrowIfCancellationRequested();
		}

		if (_store.TryConsumeInApp(entry.Id, out var allowed))
		{
			if (allowed)
			{
				return;
			}

			throw McpToolErrors.Create(McpConsent.Declined, "The user declined in Macro Deck.");
		}

		if (entry.Completion.IsCanceled)
		{
			throw McpToolErrors.Create(McpConsent.Declined, "The user did not answer in Macro Deck in time.");
		}

		throw McpToolErrors.Create(McpConsent.Pending,
			"Macro Deck is asking the user to allow or decline this. Once they answered in Macro Deck, call the "
			+ "same tool again with the same arguments.");
	}

	private async Task AwaitPage(
		McpServer server,
		RequestContext<CallToolRequestParams> context,
		string entryId,
		string fingerprint,
		ElicitResult answer,
		CancellationToken cancellationToken)
	{
		// The request state comes back from the client: the page must belong to this token and this exact call.
		var entry = _store.FindForToken(entryId, McpConsent.TokenId(context));
		if (entry is null || entry.Fingerprint != fingerprint)
		{
			throw McpToolErrors.Create(McpConsent.Declined, "The confirmation page is no longer valid.");
		}

		try
		{
			if (!answer.IsAccepted)
			{
				throw McpToolErrors.Create(McpConsent.Declined, "The user did not open the confirmation page.");
			}

			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			var remaining = entry.ExpiresAt - _timeProvider.GetUtcNow().UtcDateTime;
			timeout.CancelAfter(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
			var allowed = await entry.Completion.WaitAsync(timeout.Token);

			if (server.SessionId is not null)
			{
				await server.SendNotificationAsync(NotificationMethods.ElicitationCompleteNotification,
					new ElicitationCompleteNotificationParams { ElicitationId = entry.Id },
					cancellationToken: cancellationToken);
			}

			if (!allowed)
			{
				throw McpToolErrors.Create(McpConsent.Declined, "The user declined on the confirmation page.");
			}
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw McpToolErrors.Create(McpConsent.Declined, "The user did not answer the confirmation page in time.");
		}
		finally
		{
			_store.Abandon(entry.Id);
		}
	}

	private static string ClientName(McpServer server, RequestContext<CallToolRequestParams> context)
	{
		return context.User?.FindFirst(McpClaimTypes.TokenName)?.Value is { Length: > 0 } tokenName
			? tokenName
			: server.ClientInfo?.Name ?? string.Empty;
	}
}
