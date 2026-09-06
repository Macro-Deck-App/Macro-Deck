namespace MacroDeck.Localization;

/// <summary>
/// A localization key: the scope that owns it and its dotted name within that scope. Rendered as
/// <c>scope:Name</c>, for example <c>macrodeck:Common.Save</c> or
/// <c>plugin:com.example.spotify:Configuration.Title</c>.
/// </summary>
public readonly record struct LocalizationKey
{
	/// <summary>Creates a key.</summary>
	/// <param name="scope">The owning scope, from <see cref="LocalizationScope" />.</param>
	/// <param name="name">The dotted key name within that scope.</param>
	/// <exception cref="ArgumentException">Either argument is null or blank.</exception>
	public LocalizationKey(string scope, string name)
	{
		if (string.IsNullOrWhiteSpace(scope))
		{
			throw new ArgumentException("A localization key needs a scope.", nameof(scope));
		}

		if (string.IsNullOrWhiteSpace(name))
		{
			throw new ArgumentException("A localization key needs a name.", nameof(name));
		}

		Scope = scope;
		Name = name;
	}

	/// <summary>The scope that owns this key.</summary>
	public string Scope { get; }

	/// <summary>The dotted key name within <see cref="Scope" />.</summary>
	public string Name { get; }

	/// <summary>Creates a key in Macro Deck's own catalog.</summary>
	public static LocalizationKey MacroDeck(string name) => new(LocalizationScope.MacroDeck, name);

	/// <summary>Creates a key in <paramref name="pluginId" />'s catalog.</summary>
	public static LocalizationKey Plugin(string pluginId, string name)
		=> new(LocalizationScope.ForPlugin(pluginId), name);

	/// <summary>Parses the <c>scope:Name</c> form produced by <see cref="ToString" />.</summary>
	/// <remarks>A plugin scope contains a colon of its own, so the name is taken after the <b>last</b>
	/// colon rather than the first.</remarks>
	public static bool TryParse(string? text, out LocalizationKey key)
	{
		key = default;

		if (string.IsNullOrEmpty(text))
		{
			return false;
		}

		var separator = text!.LastIndexOf(':');
		if (separator <= 0 || separator == text.Length - 1)
		{
			return false;
		}

		var scope = text[..separator];
		if (!LocalizationScope.IsValid(scope))
		{
			return false;
		}

		key = new LocalizationKey(scope, text[(separator + 1)..]);
		return true;
	}

	/// <summary>Renders the key as <c>scope:Name</c>.</summary>
	public override string ToString() => $"{Scope}:{Name}";
}
