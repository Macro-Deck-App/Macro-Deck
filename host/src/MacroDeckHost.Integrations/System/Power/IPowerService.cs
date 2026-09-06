namespace MacroDeckHost.Integrations.System.Power;

public interface IPowerService
{
	bool IsSupported { get; }

	bool Supports(PowerOperation operation);

	Task<PowerResult> ExecuteAsync(PowerOperation operation, bool force, CancellationToken cancellationToken = default);
}
