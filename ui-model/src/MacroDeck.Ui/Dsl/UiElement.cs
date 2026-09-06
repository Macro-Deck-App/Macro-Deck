namespace MacroDeck.Ui.Dsl;

/// <summary>Base element for the UI DSL tree.</summary>
public abstract record UiElement
{
	/// <summary>
	/// Element key used to derive node identity, scope identity, and duplicate diagnostics.
	/// </summary>
	public required string Key { get; init; }

	/// <summary>Fallback element used when the requested component is unsupported.</summary>
	public UiElement? Fallback { get; init; }

	/// <summary>Required component version. <c>null</c> means version 1.</summary>
	public int? RequiredComponentVersion { get; init; }

	/// <summary>Handlers for client events accepted by this element.</summary>
	public IReadOnlyList<UiEventHandler> Events { get; init; } = [];

	/// <summary>
	/// This element's share of a non-wrapping row's main-axis space, relative to its siblings' own shares -
	/// modelled as a presentation flag on the element itself rather than a second node type, the same way
	/// <see cref="Config.UiNumericInputs" />'s <c>ShowSlider</c> and <see cref="Config.UiTextInputs" />'s
	/// <c>Multiline</c> are. Meaningful only for a direct child of a <see cref="Config.UiConfigStack" /> whose
	/// own <c>Wrap</c> is <c>false</c>; every other renderer ignores it, the same way an unset value here
	/// leaves the element sized to its own content.
	///
	/// <para>
	/// Named <c>RowWeight</c> rather than the shorter <c>Weight</c> because the widget component profile
	/// already uses that name for a text run's font weight (<see cref="Components.UiTextRun.Weight" /> and
	/// its siblings) - the same base every element in this tree shares, config and component alike, so the
	/// two meanings would otherwise collide on one member.
	/// </para>
	/// </summary>
	public UiValue<double> RowWeight { get; init; }

	/// <summary>The node type emitted by this element.</summary>
	public abstract string Type { get; }

	/// <summary>Declares the node properties contributed by this element.</summary>
	protected internal virtual void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		var names = DeclaredEvents;

		properties.Set(_eventsProperty,
			names.Count == 0 ? UiValue.None<IReadOnlyList<string>>() : UiValue.Of(names));

		properties.Set(_rowWeightProperty, RowWeight);
	}

	/// <summary>Accepted event names in advertised order without duplicates.</summary>
	protected internal virtual IReadOnlyList<string> DeclaredEvents
	{
		get
		{
			if (Events.Count == 0)
			{
				return [];
			}

			var names = new List<string>(Events.Count);

			foreach (var handler in Events)
			{
				if (!names.Contains(handler.Name, StringComparer.Ordinal))
				{
					names.Add(handler.Name);
				}
			}

			return names;
		}
	}

	private const string _eventsProperty = "events";
	private const string _rowWeightProperty = "rowWeight";
}

/// <summary>An element that emits one childless node.</summary>
public abstract record UiLeaf : UiElement;
