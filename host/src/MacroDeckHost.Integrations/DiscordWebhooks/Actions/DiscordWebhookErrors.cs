using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.DiscordWebhooks.Protocol;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.DiscordWebhooks.Errors;

namespace MacroDeckHost.Integrations.DiscordWebhooks.Actions;

internal static class DiscordWebhookErrors
{
	public static (string Code, LocalizedText Message) Describe(DiscordWebhookTargetKind kind)
	{
		return kind switch
		{
			DiscordWebhookTargetKind.EntryMissing => (ActionErrorCodes.NotFound, Strings.WebhookNotFound()),
			DiscordWebhookTargetKind.InvalidUrl => (ActionErrorCodes.InvalidParameter, Strings.InvalidUrl()),
			_ => (ActionErrorCodes.NotConfigured, Strings.NoWebhook())
		};
	}

	public static (string Code, LocalizedText Message) Describe(DiscordSendResult result)
	{
		return result.Outcome switch
		{
			DiscordSendOutcome.Rejected => (ActionErrorCodes.ProviderRejected,
				Strings.Rejected(result.Reason ?? "HTTP 400")),
			DiscordSendOutcome.Unauthorized => (ActionErrorCodes.PermissionDenied, Strings.Unauthorized()),
			DiscordSendOutcome.NotFound => (ActionErrorCodes.NotFound, Strings.Deleted()),
			DiscordSendOutcome.RateLimited => (ActionErrorCodes.Unavailable, Strings.RateLimited()),
			DiscordSendOutcome.Unreachable => (ActionErrorCodes.NotConnected, Strings.Unreachable()),
			DiscordSendOutcome.Timeout => (ActionErrorCodes.Timeout, Strings.Timeout()),
			_ => (ActionErrorCodes.ProviderError, Strings.Status(result.Status ?? 0))
		};
	}
}
