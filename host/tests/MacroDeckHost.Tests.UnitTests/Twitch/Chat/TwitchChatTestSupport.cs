using System.Globalization;
using MacroDeck.Sdk.Events;
using MacroDeckHost.Application.Twitch.Chat;
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

internal sealed class RecordingTwitchChatSink : ITwitchChatSink
{
	public List<IReadOnlyList<TwitchChatAccount>> AccountLists { get; } = [];

	public List<TwitchChatEvent> Posted { get; } = [];

	public void SetAccounts(IReadOnlyList<TwitchChatAccount> accounts) => AccountLists.Add(accounts);

	public void Post(TwitchChatEvent chatEvent) => Posted.Add(chatEvent);
}

internal sealed class RecordingTwitchEventPublisher : IEventPublisher
{
	public List<string> Published { get; } = [];

	public void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null)
		=> Published.Add(eventId);
}
