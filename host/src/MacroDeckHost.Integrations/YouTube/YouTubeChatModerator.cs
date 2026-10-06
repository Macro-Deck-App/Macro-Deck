using MacroDeck.Sdk.Logging;
using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Integrations.YouTube.Auth;
using MacroDeckHost.Integrations.YouTube.Protocol;
using Serilog;

namespace MacroDeckHost.Integrations.YouTube;

internal sealed class YouTubeChatModerator : IStreamChatModerator
{
	private static readonly ILogger _logger =
		IntegrationLog.For<YouTubeChatModerator>(YouTubeIntegration.IntegrationId);

	private static readonly HashSet<string> _refusedReasons =
		new(["liveChatBanInsertionNotAllowed", "modificationNotAllowed", "forbidden", "liveChatMessageNotFound"],
			StringComparer.Ordinal);

	private readonly Func<YouTubeAccountManager> _accounts;

	public YouTubeChatModerator(Func<YouTubeAccountManager> accounts)
	{
		_accounts = accounts;
	}

	public async Task<ChatModerationResult> ModerateAsync(
		string accountId,
		ChatModerationRequest request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		// Resolve treats an empty id as the first account; moderation must never act on a guessed account.
		if (string.IsNullOrEmpty(accountId) ||
			_accounts().Resolve(accountId) is not { ActiveLiveChatId: { } liveChatId } connection)
		{
			return ChatModerationResult.AccountUnavailable;
		}

		try
		{
			switch (request)
			{
				case { Kind: ChatModerationKind.DeleteMessage, MessageId: { Length: > 0 } messageId }:
					await connection.Api.DeleteChatMessageAsync(messageId, cancellationToken);
					connection.PostChat(new ChatMessageDeleted(connection.Account.ChannelId, messageId));
					break;
				case { Kind: ChatModerationKind.Timeout, AuthorId: { Length: > 0 } chatterId, DurationSeconds: > 0 }:
					await connection.Api.InsertBanAsync(liveChatId, chatterId, request.DurationSeconds,
						cancellationToken);
					break;
				case { Kind: ChatModerationKind.Ban, AuthorId: { Length: > 0 } chatterId }:
					var banId = await connection.Api.InsertBanAsync(liveChatId, chatterId, null, cancellationToken);
					if (banId.Length > 0)
					{
						connection.RememberBan(liveChatId, chatterId, banId);
					}

					break;
				case { Kind: ChatModerationKind.Unban, AuthorId: { Length: > 0 } chatterId }:
					// YouTube cannot list bans, so only a ban placed through Macro Deck can be lifted here.
					if (connection.FindBan(liveChatId, chatterId) is not { } knownBan)
					{
						return ChatModerationResult.Unsupported;
					}

					await connection.Api.DeleteBanAsync(knownBan, cancellationToken);
					connection.ForgetBan(liveChatId, chatterId);
					break;
				default:
					return ChatModerationResult.Refused;
			}

			return ChatModerationResult.Succeeded;
		}
		catch (YouTubeOAuthRejectedException)
		{
			_logger.Warning("YouTube chat moderation skipped: the sign-in of {Channel} expired",
				connection.Account.ChannelId);

			return ChatModerationResult.NotPermitted;
		}
		catch (YouTubeApiException exception) when (exception.IsUnauthorized ||
			exception.Reason is "insufficientPermissions")
		{
			_logger.Warning("YouTube did not grant the permission for {Kind} on {Channel}",
				request.Kind,
				connection.Account.ChannelId);

			return ChatModerationResult.MissingScope;
		}
		catch (YouTubeApiException exception) when (exception.Reason is { } reason && _refusedReasons.Contains(reason))
		{
			_logger.Warning(exception, "YouTube refused {Kind} on {Channel}", request.Kind, connection.Account.ChannelId);

			return ChatModerationResult.Refused;
		}
		catch (Exception exception) when (exception is not OperationCanceledException ||
			!cancellationToken.IsCancellationRequested)
		{
			_logger.Warning(exception, "YouTube chat moderation {Kind} failed", request.Kind);

			return ChatModerationResult.Failed;
		}
	}
}
