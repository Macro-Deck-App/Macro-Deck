using MacroDeck.Localization.Compiler;

namespace MacroDeck.Plugin.Analyzers;

/// <summary>
/// The dotted keys of a resource set arranged as the nested static classes they generate:
/// <c>Configuration.Title</c> becomes <c>Configuration</c> holding <c>Title()</c>.
/// </summary>
internal sealed class LocalizationKeyTree
{
	private readonly Dictionary<string, LocalizationKeyTree> _childrenByName = new(StringComparer.Ordinal);

	/// <summary>The keys that terminate at this level, as member name to compiled entry.</summary>
	public List<KeyValuePair<string, LocalizationCompiledEntry>> Entries { get; } =
		new();

	/// <summary>The nested classes below this level, as segment name to subtree.</summary>
	public List<KeyValuePair<string, LocalizationKeyTree>> Children { get; } =
		new();

	/// <summary>Arranges <paramref name="entries" /> into a tree. Entries arrive sorted, so members and
	/// nested classes come out in a stable order and the generated file does not churn between builds.</summary>
	public static LocalizationKeyTree Build(List<LocalizationCompiledEntry> entries)
	{
		var root = new LocalizationKeyTree();

		foreach (var entry in entries)
		{
			var segments = entry.Key.Split('.');
			var node = root;

			for (var index = 0; index < segments.Length - 1; index++)
			{
				node = node.Child(segments[index]);
			}

			node.Entries.Add(new KeyValuePair<string, LocalizationCompiledEntry>(segments[segments.Length - 1], entry));
		}

		return root;
	}

	private LocalizationKeyTree Child(string name)
	{
		if (_childrenByName.TryGetValue(name, out var existing))
		{
			return existing;
		}

		var created = new LocalizationKeyTree();
		_childrenByName.Add(name, created);
		Children.Add(new KeyValuePair<string, LocalizationKeyTree>(name, created));
		return created;
	}
}
