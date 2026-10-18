using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MacroDeck.Localization;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.Feeds.State;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Feeds.Variables;

namespace MacroDeckHost.Integrations.Feeds.Variables;

internal static partial class FeedVariables
{
	public const string Prefix = "feeds_";

	// The aggregates are named feeds_all_..., so no feed may take "all" as its key.
	public const string ReservedKey = "all";

	public const int MaxKeyLength = 30;

	public const string AllItemCountName = Prefix + ReservedKey + "_item_count";

	public const string AllUnreadCountName = Prefix + ReservedKey + "_unread_count";

	private const string AllItemCountId = "all-item-count";
	private const string AllUnreadCountId = "all-unread-count";
	private const string FeedIdPrefix = "feed-";

	private static readonly TimeSpan _refresh = TimeSpan.FromSeconds(30);

	private static readonly Slot[] _slots =
	[
		new("latest_title", VariableType.Text, Strings.LatestTitle),
		new("latest_url", VariableType.Text, Strings.LatestUrl),
		new("latest_description", VariableType.Text, Strings.LatestDescription),
		new("latest_published", VariableType.Text, Strings.LatestPublished),
		new("latest_author", VariableType.Text, Strings.LatestAuthor),
		new("item_count", VariableType.Numeric, Strings.ItemCount, 0),
		new("unread_count", VariableType.Numeric, Strings.UnreadCount, 0),
		new("last_updated", VariableType.Text, Strings.LastUpdated),
		new("update_status", VariableType.Text, Strings.UpdateStatus)
	];

	public static IReadOnlyList<string> SlotNames { get; } = [.. _slots.Select(slot => slot.Name)];

	public static IReadOnlyList<VariableDefinition> Templates { get; } =
	[
		.. Aggregates(),
		.. Declare(VariableNameTemplate.Placeholder("feed"), null, default)
	];

	public static IReadOnlyList<VariableDefinition> Aggregates()
	{
		return
		[
			VariableDefinition.Eager(AllItemCountName, VariableType.Numeric, 0, _refresh) with
			{
				Id = AllItemCountId,
				DisplayName = Strings.AllItemCount()
			},
			VariableDefinition.Eager(AllUnreadCountName, VariableType.Numeric, 0, _refresh) with
			{
				Id = AllUnreadCountId,
				DisplayName = Strings.AllUnreadCount()
			}
		];
	}

	public static IReadOnlyList<VariableDefinition> Declare(string variableKey, Guid? entryId, LocalizedText title)
	{
		return
		[
			.. _slots.Select(slot => VariableDefinition.Eager(Name(variableKey, slot.Name),
					slot.Type,
					slot.DecimalPlaces,
					_refresh)
				with
				{
					Id = entryId is { } id ? $"{FeedIdPrefix}{id:N}-{slot.Name.Replace('_', '-')}" : null,
					DisplayName = slot.Label(),
					Configuration = entryId is { } configId
						? new VariableConfiguration(configId.ToString("N"), title)
						: null
				})
		];
	}

	public static string Name(string variableKey, string slot)
	{
		return $"{Prefix}{variableKey}_{slot}";
	}

	public static VariableReading Read(string localId, IReadOnlyList<FeedSnapshot> feeds)
	{
		ArgumentNullException.ThrowIfNull(feeds);

		if (localId == AllItemCountId)
		{
			return VariableReading.Of(feeds.Sum(feed => feed.Items.Count));
		}

		if (localId == AllUnreadCountId)
		{
			return VariableReading.Of(feeds.Sum(feed => feed.UnreadCount));
		}

		if (!TryParse(localId, out var entryId, out var slot)
			|| feeds.FirstOrDefault(feed => feed.EntryId == entryId) is not { } snapshot)
		{
			return VariableReading.Unavailable;
		}

		var latest = snapshot.Items.Count > 0 ? snapshot.Items[0] : null;
		object? value = slot switch
		{
			"latest_title" => latest?.Title ?? string.Empty,
			"latest_url" => latest?.Url ?? string.Empty,
			"latest_description" => latest?.Description ?? string.Empty,
			"latest_published" => Iso(latest?.Published),
			"latest_author" => latest?.Author ?? string.Empty,
			"item_count" => snapshot.Items.Count,
			"unread_count" => snapshot.UnreadCount,
			"last_updated" => Iso(snapshot.LastUpdated),
			"update_status" => snapshot.Status,
			_ => null
		};

		return value is null ? VariableReading.Unavailable : VariableReading.Of(value);
	}

	public static bool TryParse(string localId, out Guid entryId, out string slot)
	{
		entryId = Guid.Empty;
		slot = string.Empty;

		if (!localId.StartsWith(FeedIdPrefix, StringComparison.Ordinal)
			|| localId.Length <= FeedIdPrefix.Length + 33
			|| !Guid.TryParseExact(localId.AsSpan(FeedIdPrefix.Length, 32), "N", out entryId))
		{
			return false;
		}

		slot = localId[(FeedIdPrefix.Length + 33)..].Replace('-', '_');
		return SlotNames.Contains(slot, StringComparer.Ordinal);
	}

	public static string UniqueKey(string title, ISet<string> used)
	{
		ArgumentNullException.ThrowIfNull(used);

		used.Add(ReservedKey);

		var stem = Slugify(title);
		if (stem.Length > MaxKeyLength - 3)
		{
			stem = stem[..(MaxKeyLength - 3)].TrimEnd('_');
		}

		if (stem.Length == 0)
		{
			stem = "feed";
		}

		var key = stem;
		for (var suffix = 2; !used.Add(key); suffix++)
		{
			key = $"{stem}_{suffix}";
		}

		return key;
	}

	public static string Slugify(string title)
	{
		var builder = new StringBuilder(title.Length + 4);
		foreach (var ch in title.ToLowerInvariant())
		{
			switch (ch)
			{
				case 'ä':
					builder.Append("ae");
					break;
				case 'ö':
					builder.Append("oe");
					break;
				case 'ü':
					builder.Append("ue");
					break;
				case 'ß':
					builder.Append("ss");
					break;
				case >= 'a' and <= 'z':
				case >= '0' and <= '9':
					builder.Append(ch);
					break;
				default:
					builder.Append('_');
					break;
			}
		}

		return CollapseUnderscores().Replace(builder.ToString(), "_").Trim('_');
	}

	private static string Iso(DateTimeOffset? value)
	{
		return value?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) ?? string.Empty;
	}

	[GeneratedRegex("_+")]
	private static partial Regex CollapseUnderscores();

	private sealed record Slot(string Name, VariableType Type, Func<LocalizedString> Label, int? DecimalPlaces = null);
}
