using System.Globalization;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace MacroDeck.Sdk.MusicPlayer;

/// <summary>
/// The write half of the volume and playback-position variables a music-player integration exposes.
/// Every provider drives the same <see cref="IMusicPlayer"/>, so the bodies live here once rather than
/// three times: an integration declares <see cref="Volume"/> or <see cref="Position"/> on the
/// definition and forwards its <c>SetValueAsync</c> to the matching method.
/// </summary>
public static class MusicPlayerVariableWrites
{
	private static readonly ILogger _logger = Log.ForContext(typeof(MusicPlayerVariableWrites));

	/// <summary>The write capability a volume-percent variable declares. Streams while the user drags,
	/// because hearing the level change is the point of the control.</summary>
	public static readonly VariableWriteCapability Volume = new();

	/// <summary>The write capability a playback-position variable declares. Commits on release, because
	/// every intermediate position is an audible jump.</summary>
	public static readonly VariableWriteCapability Position = new() { CommitOnRelease = true };

	/// <summary>
	/// Sets the playback volume from a variable write. The value is read as a number and clamped to the
	/// 0-100 percent range <see cref="IMusicPlayer.SetVolumeAsync"/> accepts, so the player's own reading
	/// - not the requested value - is what the host ends up holding.
	/// </summary>
	public static async ValueTask<VariableWriteResult> SetVolumeAsync(
		IMusicPlayer? player,
		object? value,
		CancellationToken cancellationToken = default)
	{
		if (player is null)
		{
			return VariableWriteResult.Unavailable();
		}

		if (!TryReadNumber(value, out var percent))
		{
			return VariableWriteResult.InvalidValue();
		}

		try
		{
			await player.SetVolumeAsync((int)Math.Round(Math.Clamp(percent, 0, 100)), cancellationToken);
			return VariableWriteResult.Applied();
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.Warning(ex, "Music player volume write failed");
			return VariableWriteResult.Failed();
		}
	}

	/// <summary>
	/// Seeks to a position, in seconds, from a variable write. A negative value is taken as the start of
	/// the track rather than refused, mirroring the seek action.
	/// </summary>
	public static async ValueTask<VariableWriteResult> SeekAsync(
		IMusicPlayer? player,
		object? value,
		CancellationToken cancellationToken = default)
	{
		if (player is null)
		{
			return VariableWriteResult.Unavailable();
		}

		if (!TryReadNumber(value, out var seconds))
		{
			return VariableWriteResult.InvalidValue();
		}

		try
		{
			await player.SeekAsync(TimeSpan.FromSeconds(Math.Max(0, seconds)), cancellationToken);
			return VariableWriteResult.Applied();
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.Warning(ex, "Music player seek write failed");
			return VariableWriteResult.Failed();
		}
	}

	/// <summary>
	/// Reads a variable write's value as a finite number. A non-finite one is refused rather than passed
	/// on, because <c>TimeSpan.FromSeconds(NaN)</c> throws and a NaN volume silently mutes.
	/// </summary>
	public static bool TryReadNumber(object? value, out double number)
	{
		number = value switch
		{
			double d => d,
			float f => f,
			int i => i,
			long l => l,
			decimal m => (double)m,
			string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) =>
				parsed,
			_ => double.NaN
		};

		return double.IsFinite(number);
	}
}
