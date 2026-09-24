using System.Net.Sockets;
using Serilog;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Usb.Native;

internal static class TaskObservation
{
	private static readonly ILogger _defaultLogger = Log.ForContext(typeof(TaskObservation));

	// A continuation that ignores its antecedent does not observe its exception; reading Exception does.
	// Teardown faults are expected and dropped; anything else is a bug and is logged before it is observed.
	public static Task Settle(Task task, ILogger? logger = null)
		=> task.ContinueWith(completed =>
			{
				if (completed.Exception is { } failure &&
					failure.Flatten().InnerExceptions.FirstOrDefault(exception => !IsExpected(exception)) is { } unexpected)
				{
					(logger ?? _defaultLogger).Error(unexpected, "Unexpected fault in a USB link task");
				}
			},
			CancellationToken.None,
			TaskContinuationOptions.ExecuteSynchronously,
			TaskScheduler.Default);

	public static void Observe(Task task, ILogger? logger = null) => _ = Settle(task, logger);

	internal static bool IsExpected(Exception exception)
		=> exception is LinkLostException or ObjectDisposedException or OperationCanceledException or
			TimeoutException or IOException or SocketException;
}
