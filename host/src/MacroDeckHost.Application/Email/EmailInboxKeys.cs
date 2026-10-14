using MacroDeckHost.Application.Variables;

namespace MacroDeckHost.Application.Email;

public sealed record EmailInboxKey(string Ref, string Slug, long Seen);

public sealed record EmailInboxKeySet(long Generation, IReadOnlyList<EmailInboxKey> Keys)
{
	public static readonly EmailInboxKeySet Empty = new(0, []);
}

public interface IEmailInboxKeyStore
{
	IReadOnlyList<EmailInboxKey> Keys { get; }

	IReadOnlyDictionary<string, string> Assign(IReadOnlyList<EmailInboxDescriptor> visible);
}

public static class EmailInboxKeys
{
	public const int MaxKeys = 128;

	public const int MaxSlugLength = 32;

	public const string FallbackSlug = "inbox";

	public static EmailInboxKeySet Assign(EmailInboxKeySet current, IReadOnlyList<EmailInboxDescriptor> visible)
	{
		var visibleRefs = visible.Select(inbox => inbox.Ref).Distinct(StringComparer.Ordinal).ToList();
		var known = current.Keys.ToDictionary(key => key.Ref, StringComparer.Ordinal);
		var lastVisible = current.Keys.Where(key => key.Seen == current.Generation)
			.Select(key => key.Ref)
			.ToHashSet(StringComparer.Ordinal);

		if (visibleRefs.All(known.ContainsKey) && lastVisible.SetEquals(visibleRefs))
		{
			return current;
		}

		var generation = current.Generation + 1;
		var visibleSet = visibleRefs.ToHashSet(StringComparer.Ordinal);
		var keys = current.Keys
			.Select(key => visibleSet.Contains(key.Ref) ? key with { Seen = generation } : key)
			.ToList();
		var slugs = keys.Select(key => key.Slug).ToHashSet(StringComparer.Ordinal);

		foreach (var inbox in visible)
		{
			if (known.ContainsKey(inbox.Ref) || keys.Any(key => key.Ref == inbox.Ref))
			{
				continue;
			}

			var slug = UniqueSlug(BaseSlug(inbox.DisplayName), slugs);
			slugs.Add(slug);
			keys.Add(new EmailInboxKey(inbox.Ref, slug, generation));
		}

		while (keys.Count > MaxKeys)
		{
			var oldest = keys.Select((key, index) => (key, index))
				.OrderBy(entry => entry.key.Seen)
				.ThenBy(entry => entry.index)
				.First();
			keys.RemoveAt(oldest.index);
		}

		return new EmailInboxKeySet(generation, keys);
	}

	public static string BaseSlug(string? displayName)
	{
		var sanitized = VariableNameSanitizer.Sanitize("x" + displayName)[1..].TrimStart('_');
		var slug = Truncate(sanitized, MaxSlugLength);
		return slug.Length == 0 ? FallbackSlug : slug;
	}

	private static string UniqueSlug(string baseSlug, HashSet<string> taken)
	{
		if (!taken.Contains(baseSlug))
		{
			return baseSlug;
		}

		for (var n = 2;; n++)
		{
			var suffix = "_" + n;
			var candidate = Truncate(baseSlug, MaxSlugLength - suffix.Length) + suffix;
			if (!taken.Contains(candidate))
			{
				return candidate;
			}
		}
	}

	private static string Truncate(string slug, int length)
	{
		return slug.Length <= length ? slug : slug[..length].TrimEnd('_');
	}
}
