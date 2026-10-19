using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Integrations.DiscordWebhooks.Configuration;

namespace MacroDeckHost.Integrations.DiscordWebhooks.Actions;

internal enum DiscordWebhookTargetKind
{
	Resolved,
	NoWebhook,
	EntryMissing,
	InvalidUrl
}

internal sealed record DiscordWebhookTarget(DiscordWebhookTargetKind Kind, Uri? Url = null, string LogName = "custom");

internal sealed class DiscordWebhookTargets
{
	public const string Custom = "custom";

	private readonly Func<IIntegrationConfig?> _config;

	public DiscordWebhookTargets(Func<IIntegrationConfig?> config)
	{
		_config = config;
	}

	public async Task<IReadOnlyList<ConfigEntrySnapshot>> GetEntriesAsync(CancellationToken cancellationToken)
	{
		return _config() is { } config ? await config.GetEntriesAsync(cancellationToken).ConfigureAwait(false) : [];
	}

	// The URL never leaves this type in a log field: callers log LogName, the entry title or "custom".
	public async Task<DiscordWebhookTarget> ResolveAsync(
		string? webhook,
		string? inlineUrl,
		CancellationToken cancellationToken)
	{
		if (Guid.TryParse(webhook, out var entryId))
		{
			var config = _config();
			if (config is null)
			{
				return new DiscordWebhookTarget(DiscordWebhookTargetKind.EntryMissing);
			}

			var entry = (await config.GetEntriesAsync(cancellationToken).ConfigureAwait(false))
				.FirstOrDefault(candidate => candidate.Id == entryId);
			if (entry is null)
			{
				return new DiscordWebhookTarget(DiscordWebhookTargetKind.EntryMissing);
			}

			var stored = await config.GetSecretAsync(entryId, DiscordWebhooksConfigKeys.Url, cancellationToken)
				.ConfigureAwait(false);

			return DiscordWebhookUrl.TryParse(stored, out var storedUri)
				? new DiscordWebhookTarget(DiscordWebhookTargetKind.Resolved, storedUri, entry.Title)
				: new DiscordWebhookTarget(DiscordWebhookTargetKind.InvalidUrl, LogName: entry.Title);
		}

		if (string.IsNullOrWhiteSpace(inlineUrl))
		{
			return new DiscordWebhookTarget(DiscordWebhookTargetKind.NoWebhook);
		}

		return DiscordWebhookUrl.TryParse(inlineUrl, out var uri)
			? new DiscordWebhookTarget(DiscordWebhookTargetKind.Resolved, uri)
			: new DiscordWebhookTarget(DiscordWebhookTargetKind.InvalidUrl);
	}
}
