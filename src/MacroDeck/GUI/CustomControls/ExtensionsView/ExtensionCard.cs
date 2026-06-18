namespace SuchByte.MacroDeck.GUI.CustomControls.ExtensionsView;

public enum CardActionStyle
{
    Primary,
    Accent,
    Link
}

/// <summary>
/// A single clickable element rendered on a card (a button or a link).
/// </summary>
public sealed class CardAction
{
    public string Label { get; set; }
    public bool Enabled { get; set; } = true;
    public CardActionStyle Style { get; set; } = CardActionStyle.Primary;
    public Action OnClick { get; set; }
}

/// <summary>
/// View-model for one extension card. Both the online store and the installed-extensions view
/// build a list of these and hand them to <see cref="ExtensionGrid"/>, which owner-draws them.
/// </summary>
public sealed class ExtensionCard
{
    public string Title { get; set; } = "";

    /// <summary>Author (store) or installed version (installed view).</summary>
    public string Subtitle { get; set; } = "";

    /// <summary>Longer text shown on store cards. Empty for installed cards.</summary>
    public string Description { get; set; } = "";

    public string BadgeText { get; set; } = "";
    public Color BadgeColor { get; set; } = Color.FromArgb(0, 95, 173);

    /// <summary>Short status line shown on installed cards (e.g. Enabled / Update available).</summary>
    public string StatusText { get; set; } = "";
    public Color StatusColor { get; set; } = Color.FromArgb(0, 192, 0);

    public Color BackgroundTint { get; set; } = Color.FromArgb(65, 65, 65);

    /// <summary>
    /// Loads the card's icon off the UI thread. The grid caches the result for visible cards.
    /// </summary>
    public Func<CancellationToken, Task<Image>> LoadIconAsync { get; set; }

    /// <summary>
    /// When true the grid owns the image returned by <see cref="LoadIconAsync"/> and disposes it
    /// when the card scrolls out of view. Set false for images owned elsewhere (e.g. a plugin's
    /// in-memory icon) so the grid never disposes them.
    /// </summary>
    public bool DisposableIcon { get; set; } = true;

    public List<CardAction> Actions { get; } = new();

    /// <summary>Lowercased text used for client-side filtering.</summary>
    public string SearchText { get; set; } = "";
}
