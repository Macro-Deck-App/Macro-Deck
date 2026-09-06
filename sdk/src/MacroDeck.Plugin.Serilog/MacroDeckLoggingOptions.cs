using System.ComponentModel.DataAnnotations;
using MacroDeck.Plugin.Protocol.Limits;
using Serilog.Events;

namespace MacroDeck.Plugin.Serilog;

/// <summary>
/// Tunables for <see cref="MacroDeckLoggingBuilderExtensions.UseMacroDeckLogging" />,
/// bound from the <see cref="SectionName" /> configuration section.
/// </summary>
public sealed class MacroDeckLoggingOptions
{
	/// <summary>The configuration section these options bind to.</summary>
	public const string SectionName = "MacroDeck:Plugin:Logging";

	/// <summary>
	/// The minimum level forwarded to the host. Independent of whatever minimum level the author's own
	/// <c>configure</c> callback sets on the pipeline as a whole - this only restricts what the Macro
	/// Deck sink itself accepts, so a lower pipeline minimum can still reach a local file sink without
	/// also reaching the host.
	/// </summary>
	[Required]
	public LogEventLevel MinimumLevel { get; set; } = LogEventLevel.Information;

	/// <summary>
	/// How many events one <c>log.publish</c> batch tries to carry. Capped again at send time by
	/// <see cref="ProtocolLimits.MaxLogEventsPerBatch" />, whichever is smaller - this only lets a
	/// plugin author ship smaller batches more often, never larger ones than the protocol allows.
	/// </summary>
	[Range(1, ProtocolLimits.MaxLogEventsPerBatch)]
	public int BatchSize { get; set; } = ProtocolLimits.MaxLogEventsPerBatch;

	/// <summary>How often a partial batch is flushed even when it never fills up.</summary>
	public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(2);

	/// <summary>
	/// Total capacity across both internal queues combined, split roughly 25/75 between the
	/// priority (warning and above) and bulk queues - see the sink's remarks on why the split exists.
	/// </summary>
	[Range(2, int.MaxValue)]
	public int QueueCapacity { get; set; } = 2000;

	/// <summary>
	/// Whether undelivered batches are tee'd to a small local file while the host is unreachable. See
	/// this package's README for why that file is a diagnostic tail and not a replay buffer.
	/// </summary>
	public bool EnableFallbackFile { get; set; } = true;

	/// <summary>Hard byte cap on the fallback file. Once reached, the oldest content is dropped to make
	/// room for the newest rather than the file growing without bound or freezing on stale entries.</summary>
	[Range(1, int.MaxValue)]
	public int FallbackFileMaxBytes { get; set; } = 1024 * 1024;
}
