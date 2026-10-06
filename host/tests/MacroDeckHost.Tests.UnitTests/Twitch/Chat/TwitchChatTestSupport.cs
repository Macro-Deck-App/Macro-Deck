using System.Globalization;
using MacroDeck.Sdk.Events;
using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Integrations.Twitch;
using MacroDeckHost.Integrations.Twitch.Auth;

namespace MacroDeckHost.Tests.UnitTests.Twitch.Chat;

internal static class TwitchChatTestSupport
{
	public static void AddAccount(RecordingIntegrationConfig config, string userId, string login)
		=> config.AddEntry($"Twitch ({login})",
			new Dictionary<string, string?>(StringComparer.Ordinal)
			{
				[TwitchConfigKeys.ClientId] = "client-id",
				[TwitchConfigKeys.UserId] = userId,
				[TwitchConfigKeys.Login] = login,
				[TwitchConfigKeys.DisplayName] = login,
				[TwitchConfigKeys.Scopes] = TwitchScopes.Requested,
				[TwitchConfigKeys.ExpiresAt] =
					DateTimeOffset.UtcNow.AddHours(4).ToString("o", CultureInfo.InvariantCulture)
			},
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				[TwitchConfigKeys.AccessToken] = "access",
				[TwitchConfigKeys.RefreshToken] = "refresh"
			});
}

internal sealed class RecordingTwitchChatSink : IStreamChatSink
{
	public List<IReadOnlyList<ChatAccount>> AccountLists { get; } = [];

	public List<ChatEvent> Posted { get; } = [];

	public void SetAccounts(IReadOnlyList<ChatAccount> accounts) => AccountLists.Add(accounts);

	public void Post(ChatEvent chatEvent) => Posted.Add(chatEvent);
}

internal sealed class RecordingTwitchEventPublisher : IEventPublisher
{
	public List<string> Published { get; } = [];

	public void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null)
		=> Published.Add(eventId);
}
