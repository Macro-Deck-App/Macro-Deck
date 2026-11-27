using System.Runtime.Versioning;
using System.Text;
using MacroDeckHost.Platform.Native;
using MacroDeckHost.Platform.Processes;

namespace MacroDeckHost.Integrations.Clipboard.Native;

[SupportedOSPlatform("linux")]
internal sealed class LinuxClipboardBackend : IClipboardBackend
{
	private const int ListingLimit = 64 * 1024;
	private const int SignatureLength = 8;

	private static readonly TimeSpan _reprobeInterval = TimeSpan.FromSeconds(10);

	private readonly bool _wayland;
	private readonly Func<string, bool> _commandExists;
	private readonly Lock _gate = new();

	private ClipboardAvailability? _availability;
	private long _nextProbe;

	public LinuxClipboardBackend()
		: this(LinuxDisplaySession.IsWayland, ProcessRunner.CommandExists)
	{
	}

	internal LinuxClipboardBackend(bool wayland, Func<string, bool> commandExists)
	{
		_wayland = wayland;
		_commandExists = commandExists;
	}

	public ClipboardAvailability Availability
	{
		get
		{
			lock (_gate)
			{
				var stale = _availability is null
					|| !_availability.IsAvailable && Environment.TickCount64 >= _nextProbe;
				if (stale)
				{
					_availability = LinuxClipboardCommands.Probe(_wayland, _commandExists);
					_nextProbe = Environment.TickCount64 + (long)_reprobeInterval.TotalMilliseconds;
				}

				return _availability!;
			}
		}
	}

	public async Task<ClipboardContentKinds> ReadKindsAsync(CancellationToken cancellationToken)
	{
		EnsureAvailable();
		var result = await ClipboardProcess.ReadAsync(LinuxClipboardCommands.ListTypes(_wayland),
				ListingLimit,
				cancellationToken)
			.ConfigureAwait(false);
		if (result.IsTruncated)
		{
			throw new ClipboardException(ClipboardFailure.Failed, "The clipboard type listing is too long");
		}

		if (result.ExitCode != 0)
		{
			return LinuxClipboardCommands.MeansNoContent(result.Error)
				? ClipboardContentKinds.None
				: throw new ClipboardException(ClipboardFailure.Failed, "Listing the clipboard types failed");
		}

		var kinds = LinuxClipboardCommands.ParseKinds(Encoding.UTF8.GetString(result.Output));
		if (kinds != ClipboardContentKinds.Text)
		{
			return kinds;
		}

		var probe = await ClipboardProcess.ReadAsync(LinuxClipboardCommands.ReadText(_wayland),
				1,
				cancellationToken)
			.ConfigureAwait(false);
		return LinuxClipboardCommands.KindsAfterTextProbe(kinds, probe);
	}

	public async Task<ClipboardTextReading> ReadTextAsync(int maxChars, CancellationToken cancellationToken)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(maxChars);
		var kinds = await ReadKindsAsync(cancellationToken).ConfigureAwait(false);
		if (!kinds.HasFlag(ClipboardContentKinds.Text))
		{
			return ClipboardTextReading.None;
		}

		// A UTF-16 character never takes more than three UTF-8 bytes, so this cap cannot cut a fitting text.
		var byteLimit = (int)Math.Min(3L * maxChars, Array.MaxLength);
		var result = await ClipboardProcess.ReadAsync(LinuxClipboardCommands.ReadText(_wayland),
				byteLimit,
				cancellationToken)
			.ConfigureAwait(false);
		if (result.IsTruncated)
		{
			return ClipboardTextReading.TooLarge;
		}

		if (result.ExitCode != 0)
		{
			return LinuxClipboardCommands.MeansNoContent(result.Error)
				? ClipboardTextReading.None
				: throw new ClipboardException(ClipboardFailure.Failed, "Reading the clipboard text failed");
		}

		var text = Encoding.UTF8.GetString(result.Output);
		return text.Length > maxChars ? ClipboardTextReading.TooLarge : ClipboardTextReading.Of(text);
	}

	public Task<ClipboardWriteReceipt> SetTextAsync(string text, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(text);
		return CopyAsync(LinuxClipboardCommands.Copy(_wayland, null), Encoding.UTF8.GetBytes(text), cancellationToken);
	}

	public async Task<ClipboardWriteReceipt> SetImageAsync(string path, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(path);
		if (!File.Exists(path))
		{
			throw new ClipboardException(ClipboardFailure.NotFound);
		}

		var type = LinuxClipboardCommands.ImageType(path)
			?? throw new ClipboardException(ClipboardFailure.UnsupportedFormat);
		var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
		var header = bytes.AsSpan(0, Math.Min(bytes.Length, SignatureLength));
		if (!LinuxClipboardCommands.HasImageSignature(type, header))
		{
			throw new ClipboardException(ClipboardFailure.UnsupportedFormat);
		}

		return await CopyAsync(LinuxClipboardCommands.Copy(_wayland, type), bytes, cancellationToken)
			.ConfigureAwait(false);
	}

	public Task<ClipboardWriteReceipt> SetFilesAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(paths);
		return CopyAsync(LinuxClipboardCommands.Copy(_wayland, LinuxClipboardCommands.UriListType),
			Encoding.UTF8.GetBytes(LinuxClipboardCommands.BuildUriList(paths)),
			cancellationToken);
	}

	public Task<ClipboardWriteReceipt> ClearAsync(CancellationToken cancellationToken)
	{
		return CopyAsync(LinuxClipboardCommands.Clear(_wayland), [], cancellationToken);
	}

	public IClipboardChangeSource CreateChangeSource()
	{
		return _wayland ? new WaylandClipboardChangeSource() : new X11ClipboardChangeSource();
	}

	private async Task<ClipboardWriteReceipt> CopyAsync(
		ClipboardCommand command,
		byte[] input,
		CancellationToken cancellationToken)
	{
		EnsureAvailable();
		var exitCode = await ClipboardProcess.WriteAsync(command, input, cancellationToken).ConfigureAwait(false);
		return exitCode == 0
			? new ClipboardWriteReceipt(null)
			: throw new ClipboardException(ClipboardFailure.Failed, $"{command.FileName} exited with {exitCode}");
	}

	private void EnsureAvailable()
	{
		if (!Availability.IsAvailable)
		{
			throw new ClipboardException(ClipboardFailure.Unavailable);
		}
	}
}
