namespace MacroDeckHost.Application.Logging;

public interface ILogFileReader
{
	LogPage ReadPage(LogQuery query, LogCursor? before, int limit);

	LogTailBatch ReadAfter(LogCursor position);

	LogCursor CurrentEnd();

	IReadOnlyList<LogSourceSummary> ReadSources();
}
