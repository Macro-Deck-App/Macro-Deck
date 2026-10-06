using MacroDeck.Sdk.Logging;
using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Integrations.Twitch.Protocol;
using Serilog;
using TwitchLib.Api.Core.Exceptions;

namespace MacroDeckHost.Integrations.Twitch;

internal sealed class TwitchChatModerator : IStreamChatModerator
{
	private static readonly ILogger _logger = IntegrationLog.For<TwitchChatModerator>(TwitchIntegration.IntegrationId);

	private readonly Func<TwitchAccountManager> _accounts;

	public TwitchChatModerator(Func<TwitchAccountManager> accounts)
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
			_accounts().Resolve(accountId) is not { State.IsConnected: true } connection)
		{
			return ChatModerationResult.AccountUnavailable;
		}

		var channel = connection.Account.UserId;

		try
		{
			switch (request)
			{
				// A missing message id makes Helix delete every message in the chat.
				case { Kind: ChatModerationKind.DeleteMessage, MessageId: { Length: > 0 } messageId }:
					await connection.Helix.DeleteChatMessagesAsync(channel, channel, messageId, cancellationToken);
					break;
				case { Kind: ChatModerationKind.Timeout, AuthorId: { Length: > 0 } chatterId, DurationSeconds: > 0 }:
					await connection.Helix.BanUserAsync(channel, channel, chatterId, request.DurationSeconds, null,
						cancellationToken);
					break;
				case { Kind: ChatModerationKind.Ban, AuthorId: { Length: > 0 } chatterId }:
					await connection.Helix.BanUserAsync(channel, channel, chatterId, null, null, cancellationToken);
					break;
				case { Kind: ChatModerationKind.Unban, AuthorId: { Length: > 0 } chatterId }:
					await connection.Helix.UnbanUserAsync(channel, channel, chatterId, cancellationToken);
					break;
				default:
					return ChatModerationResult.Refused;
			}

			return ChatModerationResult.Succeeded;
		}
		catch (TwitchScopeException)
		{
			_logger.Warning("Twitch chat moderation skipped: {Account} did not grant the permission for it",
				connection.Account.Login);

			return ChatModerationResult.MissingScope;
		}
		catch (BadTokenException exception)
		{
			_logger.Warning(exception, "Twitch refused {Kind} for {Account}", request.Kind, connection.Account.Login);

			return ChatModerationResult.NotPermitted;
		}
		catch (Exception exception) when (exception is BadRequestException or BadParameterException)
		{
			_logger.Warning(exception, "Twitch rejected {Kind} for {Account}", request.Kind, connection.Account.Login);

			return ChatModerationResult.Refused;
		}
		catch (Exception exception) when (exception is not OperationCanceledException ||
			!cancellationToken.IsCancellationRequested)
		{
			_logger.Warning(exception, "Twitch chat moderation {Kind} failed", request.Kind);

			return ChatModerationResult.Failed;
		}
	}
}
