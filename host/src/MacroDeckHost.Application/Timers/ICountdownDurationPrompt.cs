namespace MacroDeckHost.Application.Timers;

public interface ICountdownDurationPrompt
{
	Task<int?> AskAsync(Guid widgetId, string originClientId, int? initialSeconds, CancellationToken cancellationToken);
}
