using System.Globalization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Email;
using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Application.Email;
using MacroDeckHost.Application.Integrations;
using VariableStrings = MacroDeckHost.Localization.AppStrings.Integrations.Email.Variables;

namespace MacroDeckHost.Infrastructure.SystemIntegrations.Email;

internal enum EmailVariableStat
{
	UnreadCount,

	TotalCount,

	LatestSender,

	LatestSubject,

	LatestReceivedAt,

	IsConnected
}

internal sealed record EmailInboxVariableReader(string InboxRef, EmailVariableStat Stat);

internal sealed record EmailInboxVariableSet(
	IReadOnlyList<VariableDefinition> Definitions,
	IReadOnlyDictionary<string, EmailInboxVariableReader> Readers,
	IReadOnlyDictionary<string, string> Declared,
	IReadOnlyDictionary<string, string> Names,
	IReadOnlyDictionary<string, string> Removed,
	int Candidates)
{
	public static readonly EmailInboxVariableSet Initial = new(EmailInboxVariables.GlobalDefinitions,
		new Dictionary<string, EmailInboxVariableReader>(StringComparer.Ordinal),
		new Dictionary<string, string>(StringComparer.Ordinal),
		new Dictionary<string, string>(StringComparer.Ordinal),
		new Dictionary<string, string>(StringComparer.Ordinal),
		0);

	public bool IsCapped => Candidates > Declared.Count;

	public bool SameDeclarationsAs(EmailInboxVariableSet other)
	{
		return Declared.Count == other.Declared.Count
			&& Declared.All(entry
				=> other.Declared.TryGetValue(entry.Key, out var slug)
				&& slug == entry.Value
				&& Names.GetValueOrDefault(entry.Key) == other.Names.GetValueOrDefault(entry.Key));
	}
}

internal static class EmailInboxVariables
{
	public const string UnreadCountId = "email-unread-count";

	public const string InboxCountId = "email-inbox-count";

	private static readonly TimeSpan _refreshInterval = TimeSpan.FromMinutes(1);

	private static readonly (EmailVariableStat Stat, string Suffix)[] _stats =
	[
		(EmailVariableStat.UnreadCount, "unread_count"),
		(EmailVariableStat.TotalCount, "total_count"),
		(EmailVariableStat.LatestSender, "latest_sender"),
		(EmailVariableStat.LatestSubject, "latest_subject"),
		(EmailVariableStat.LatestReceivedAt, "latest_received_at"),
		(EmailVariableStat.IsConnected, "is_connected")
	];

	public static IReadOnlyList<VariableDefinition> GlobalDefinitions { get; } =
	[
		VariableDefinition.Eager("email_unread_count", VariableType.Numeric, 0, _refreshInterval)
			with
			{
				DisplayName = VariableStrings.UnreadCount()
			},
		VariableDefinition.Eager("email_inbox_count", VariableType.Numeric, 0, _refreshInterval)
			with
			{
				DisplayName = VariableStrings.InboxCount()
			}
	];

	public static string Name(string slug, EmailVariableStat stat)
	{
		return $"email_{slug}_{_stats.Single(entry => entry.Stat == stat).Suffix}";
	}

	public static IEnumerable<string> DefinitionIds(string slug)
	{
		return _stats.Select(entry => VariableDefinitionId.FromName(Name(slug, entry.Stat))).OfType<string>();
	}

	public static EmailInboxVariableSet Compose(
		EmailInboxesSnapshot snapshot,
		IReadOnlyList<EmailInboxKey> keys,
		IIntegrationRegistry integrations,
		EmailInboxVariableSet previous,
		IReadOnlySet<string> persisted)
	{
		var byId = integrations.Integrations
			.GroupBy(integration => integration.Id, StringComparer.Ordinal)
			.ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
		var listings = new Dictionary<string, HashSet<string>?>(StringComparer.Ordinal);
		var candidates = new List<(string Ref, string Slug)>();
		var removed = new Dictionary<string, string>(StringComparer.Ordinal);

		foreach (var (inboxRef, slug) in Ordered(snapshot, keys))
		{
			if (snapshot.Find(inboxRef) is not null)
			{
				candidates.Add((inboxRef, slug));
				continue;
			}

			var declaredBefore = previous.Declared.ContainsKey(inboxRef);
			if (!declaredBefore && !persisted.Contains(inboxRef))
			{
				continue;
			}

			var integrationId = IntegrationIdOf(inboxRef);
			if (!byId.TryGetValue(integrationId, out var integration))
			{
				if (declaredBefore)
				{
					removed[inboxRef] = slug;
				}

				continue;
			}

			if (!integrations.IsEnabled(integrationId))
			{
				candidates.Add((inboxRef, slug));
				continue;
			}

			if (!listings.TryGetValue(integrationId, out var listed))
			{
				listed = Listing(integration);
				listings[integrationId] = listed;
			}

			if (listed is null || listed.Contains(inboxRef))
			{
				candidates.Add((inboxRef, slug));
			}
			else
			{
				removed[inboxRef] = slug;
			}
		}

		var declared = candidates.Take(EmailWidgetTypes.MaxInboxesWithVariables).ToList();
		var names = new Dictionary<string, string>(StringComparer.Ordinal);
		var readers = new Dictionary<string, EmailInboxVariableReader>(StringComparer.Ordinal);
		var definitions = new List<VariableDefinition>(GlobalDefinitions);

		foreach (var (inboxRef, slug) in declared)
		{
			var name = snapshot.Find(inboxRef)?.Inbox.DisplayName ?? previous.Names.GetValueOrDefault(inboxRef) ?? slug;
			names[inboxRef] = name;

			foreach (var (stat, _) in _stats)
			{
				var definition = Declare(slug, name, stat);
				definitions.Add(definition);
				readers[definition.ResolvedId!] = new EmailInboxVariableReader(inboxRef, stat);
			}
		}

		return new EmailInboxVariableSet(definitions,
			readers,
			declared.ToDictionary(entry => entry.Ref, entry => entry.Slug, StringComparer.Ordinal),
			names,
			removed,
			candidates.Count);
	}

	public static VariableReading Read(EmailInboxesSnapshot snapshot, EmailInboxVariableSet set, string localId)
	{
		switch (localId)
		{
			case UnreadCountId:
				return VariableReading.Of(snapshot.Inboxes.Sum(state => state.UnreadCount));
			case InboxCountId:
				return VariableReading.Of(snapshot.Inboxes.Count);
		}

		if (!set.Readers.TryGetValue(localId, out var reader) || snapshot.Find(reader.InboxRef) is not { } state)
		{
			return VariableReading.Unavailable;
		}

		if (reader.Stat == EmailVariableStat.IsConnected)
		{
			return VariableReading.Of(state.IsConnected);
		}

		if (!state.HasData)
		{
			return VariableReading.Unavailable;
		}

		var latest = state.Latest.Count > 0 ? state.Latest[0] : null;

		return reader.Stat switch
		{
			EmailVariableStat.UnreadCount => VariableReading.Of(state.UnreadCount),
			EmailVariableStat.TotalCount => VariableReading.Of(state.TotalCount),
			EmailVariableStat.LatestSender => VariableReading.Of(latest?.SenderName
				?? latest?.SenderAddress ?? string.Empty),
			EmailVariableStat.LatestSubject => VariableReading.Of(latest?.Subject ?? string.Empty),
			EmailVariableStat.LatestReceivedAt => VariableReading.Of(latest is null
				? string.Empty
				: latest.ReceivedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)),
			_ => VariableReading.Unavailable
		};
	}

	private static VariableDefinition Declare(string slug, string inbox, EmailVariableStat stat)
	{
		var name = Name(slug, stat);

		return stat switch
		{
			EmailVariableStat.UnreadCount => VariableDefinition.Eager(name, VariableType.Numeric, 0, _refreshInterval)
				with
				{
					DisplayName = VariableStrings.InboxUnreadCount(inbox)
				},
			EmailVariableStat.TotalCount => VariableDefinition.Eager(name, VariableType.Numeric, 0, _refreshInterval)
				with
				{
					DisplayName = VariableStrings.InboxTotalCount(inbox)
				},
			EmailVariableStat.LatestSender => VariableDefinition.Eager(name,
					VariableType.Text,
					refreshInterval: _refreshInterval)
				with
				{
					DisplayName = VariableStrings.InboxLatestSender(inbox)
				},
			EmailVariableStat.LatestSubject => VariableDefinition.Eager(name,
					VariableType.Text,
					refreshInterval: _refreshInterval)
				with
				{
					DisplayName = VariableStrings.InboxLatestSubject(inbox)
				},
			EmailVariableStat.LatestReceivedAt => VariableDefinition.Eager(name,
					VariableType.Text,
					refreshInterval: _refreshInterval)
				with
				{
					DisplayName = VariableStrings.InboxLatestReceivedAt(inbox)
				},
			_ => VariableDefinition.Eager(name, VariableType.Boolean, refreshInterval: _refreshInterval)
				with
				{
					DisplayName = VariableStrings.InboxConnected(inbox)
				}
		};
	}

	private static IEnumerable<(string Ref, string Slug)> Ordered(
		EmailInboxesSnapshot snapshot,
		IReadOnlyList<EmailInboxKey> keys)
	{
		var seen = new HashSet<string>(StringComparer.Ordinal);

		foreach (var key in keys)
		{
			if (seen.Add(key.Ref))
			{
				yield return (key.Ref, key.Slug);
			}
		}

		foreach (var state in snapshot.Inboxes)
		{
			if (seen.Add(state.Ref))
			{
				yield return (state.Ref, state.Slug);
			}
		}
	}

	private static HashSet<string>? Listing(IIntegration integration)
	{
		if (integration is not IEmailInboxProvider provider)
		{
			return new HashSet<string>(StringComparer.Ordinal);
		}

		if (!integration.IsInitialized)
		{
			return null;
		}

		try
		{
			return provider.GetInboxes()
				.Where(inbox => inbox is not null)
				.Select(inbox => QualifiedId.TryCreate(integration.Id, inbox.Id, out var id) ? id.ToString() : null)
				.OfType<string>()
				.ToHashSet(StringComparer.Ordinal);
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static string IntegrationIdOf(string inboxRef)
	{
		var separator = inboxRef.IndexOf(QualifiedId.Separator, StringComparison.Ordinal);
		return separator < 0 ? inboxRef : inboxRef[..separator];
	}
}
