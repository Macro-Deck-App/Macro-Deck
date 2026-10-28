namespace MacroDeckHost.Application.Secrets.Approvals;

public interface ISecretLineage
{
	Guid Root(Guid secretId);

	void RecordClone(Guid sourceId, Guid cloneId);
}
