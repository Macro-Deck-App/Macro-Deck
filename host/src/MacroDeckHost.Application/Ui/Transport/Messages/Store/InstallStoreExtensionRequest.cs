using MacroDeckHost.Application.Store.Model;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Store;

public class InstallStoreExtensionRequest
{
	public StoreExtensionKind Kind { get; set; }

	public string PackageId { get; set; } = string.Empty;

	public string? Version { get; set; }

	/// <summary>Set only after the user accepted an explicit unsigned-package warning for this one
	/// install. The host decides whether it means anything: it applies to plugins, only while developer
	/// mode is on host-side, and only to a package that carries no signature at all.</summary>
	public bool AllowUnsigned { get; set; }
}
