using MacroDeckHost.Application.Portable;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Portable;

public abstract class ExportArchiveRequest
{
	public bool IncludeIcons { get; set; } = true;

	public bool IncludeSecrets { get; set; }

	public string? Password { get; set; }

	public virtual PortableExportOptions ToOptions()
		=> new()
		{
			IncludeIcons = IncludeIcons,
			IncludeSecrets = IncludeSecrets,
			Password = Password
		};
}
