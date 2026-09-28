using MacroDeck.Sdk.Logging;
using MacroDeckHost.Application.Twitch.Chat;
using MacroDeckHost.Integrations.Twitch.Protocol;
using Serilog;
using TwitchLib.Api.Core.Exceptions;

namespace MacroDeckHost.Integrations.Twitch;

internal sealed class TwitchChatModerator : ITwitchChatModerator
{
	private static readonly ILogger _logger = IntegrationLog.For<TwitchChatModerator>(TwitchIntegration.IntegrationId);

	private readonly Func<TwitchAccountManager> _accounts;

	public TwitchChatModerator(Func<TwitchAccountManager> accounts)
	{
		_accounts = accounts;
	}

	public async Task<TwitchChatModerationResult> ModerateAsync(
		string accountId,
		TwitchChatModerationRequest request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		// Resolve treats an empty id as the first account; moderation must never act on a guessed account.
		if (string.IsNullOrEmpty(accountId) ||
			_accounts().Resolve(accountId) is not { State.IsConnected: true } connection)
		{
			return TwitchChatModerationResult.AccountUnavailable;
		}

		var channel = connection.Account.UserId;

		try
		{
			switch (request)
			{
				// A missing message id makes Helix delete every message in the chat.
				case { Kind: TwitchChatModerationKind.DeleteMessage, MessageId: { Length: > 0 } messageId }:
					await connection.Helix.DeleteChatMessagesAsync(channel, channel, messageId, cancellationToken);
					break;
				case { Kind: TwitchChatModerationKind.Timeout, ChatterId: { Length: > 0 } chatterId, DurationSeconds: > 0 }:
					await connection.Helix.BanUserAsync(channel, channel, chatterId, request.DurationSeconds, null,
						cancellationToken);
					break;
				case { Kind: TwitchChatModerationKind.Ban, ChatterId: { Length: > 0 } chatterId }:
					await connection.Helix.BanUserAsync(channel, channel, chatterId, null, null, cancellationToken);
					break;
				case { Kind: TwitchChatModerationKind.Unban, ChatterId: { Length: > 0 } chatterId }:
					await connection.Helix.UnbanUserAsync(channel, channel, chatterId, cancellationToken);
					break;
				default:
					return TwitchChatModerationResult.Refused;
			}

			return TwitchChatModerationResult.Succeeded;
		}
		catch (TwitchScopeException)
		{
			_logger.Warning("Twitch chat moderation skipped: {Account} did not grant the permission for it",
				connection.Account.Login);

			return TwitchChatModerationResult.MissingScope;
		}
		catch (BadTokenException exception)
		{
			_logger.Warning(exception, "Twitch refused {Kind} for {Account}", request.Kind, connection.Account.Login);

			return TwitchChatModerationResult.NotPermitted;
		}
		catch (Exception exception) when (exception is BadRequestException or BadParameterException)
		{
			_logger.Warning(exception, "Twitch rejected {Kind} for {Account}", request.Kind, connection.Account.Login);

			return TwitchChatModerationResult.Refused;
		}
		catch (Exception exception) when (exception is not OperationCanceledException ||
			!cancellationToken.IsCancellationRequested)
		{
			_logger.Warning(exception, "Twitch chat moderation {Kind} failed", request.Kind);

			return TwitchChatModerationResult.Failed;
		}
	}
}
