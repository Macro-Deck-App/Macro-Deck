using MacroDeckHost.Application.Persistence.Icons;

namespace MacroDeckHost.Application.Persistence;

public interface IIconImportBatchStore
{
	IReadOnlyList<IconImportBatchFile> LoadAll();

	void Save(IconImportBatchFile batch);

	void Delete(Guid batchId);
}
