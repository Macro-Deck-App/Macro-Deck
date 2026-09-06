using MacroDeck.Plugin.Cli;
using MacroDeck.Plugin.Cli.Runtime;

// One shutdown signal for the whole process. Every command except run simply lets the resulting
// OperationCanceledException propagate to CliEntryPoint.RunAsync's top-level catch, which reports
// ExitCode.Cancelled; run alone catches it itself first, to run the documented supervisor shutdown
// sequence - see RunSession.
using var shutdownSignal = new ShutdownSignal();

return await CliEntryPoint.RunAsync(args, Console.Out, Console.Error, ct: shutdownSignal.Token).ConfigureAwait(false);
