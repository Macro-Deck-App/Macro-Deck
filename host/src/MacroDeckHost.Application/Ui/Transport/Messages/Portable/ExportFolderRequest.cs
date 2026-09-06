using MacroDeckHost.Application.Portable;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Portable;

public sealed class ExportFolderRequest : ExportArchiveRequest
{
	public bool IncludeSubfolders { get; set; }

	public override PortableExportOptions ToOptions()
		=> base.ToOptions() with { IncludeSubfolders = IncludeSubfolders };
}
