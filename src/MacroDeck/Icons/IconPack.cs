namespace SuchByte.MacroDeck.Icons;

public class IconPack
{
    public string PackageId;

    /// <summary>
    /// Name of the icon pack
    /// </summary>
    public string Name;

    /// <summary>
    /// Author of the icon pack
    /// </summary>
    public string Author;

    /// <summary>
    /// Version of the icon pack
    /// </summary>
    public string Version;

    /// <summary>
    /// A list containing all icons of the icon pack
    /// </summary>
    public List<Icon> Icons;

    /// <summary>
    /// Icon displayed in the extension manager. Not populated permanently to avoid keeping a
    /// bitmap per icon pack in memory; consumers generate it on demand via
    /// <see cref="SuchByte.MacroDeck.Utils.IconPackPreview.GeneratePreviewImage"/> and dispose it.
    /// </summary>
    public Image IconPackIcon { get; set; }

    /// <summary>
    /// Disabled the editing of the icon pack
    /// </summary>
    public bool ExtensionStoreManaged { get; set; } = false;

    /// <summary>
    /// Hides the icon pack
    /// </summary>
    public bool Hidden { get; set; } = false;
}
