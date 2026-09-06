using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Applications;

public interface IFolderRevealService
{
	Result<FolderRevealError> Reveal(string path);
}
