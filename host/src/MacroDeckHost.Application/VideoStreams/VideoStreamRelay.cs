using System.Buffers.Text;
using System.Security.Cryptography;

namespace MacroDeckHost.Application.VideoStreams;

public sealed class VideoStreamRelay : IVideoStreamRelay
{
	public const string PathPrefix = "/api/video-streams/relay/";

	public const string HttpClientName = "video-stream-relay";

	public const int MaxLeasesPerSession = 8;

	public const int MaxLeases = 64;

	private readonly Dictionary<string, Entry> _bySession = new(StringComparer.Ordinal);
	private readonly Dictionary<string, Entry> _byToken = new(StringComparer.Ordinal);
	private readonly Lock _gate = new();
	private int _leases;

	public string Arm(string sessionId, Uri upstream, string transport)
	{
		ArgumentException.ThrowIfNullOrEmpty(sessionId);
		ArgumentNullException.ThrowIfNull(upstream);
		ArgumentException.ThrowIfNullOrEmpty(transport);
		if (!upstream.IsAbsoluteUri || (upstream.Scheme != Uri.UriSchemeHttp && upstream.Scheme != Uri.UriSchemeHttps))
		{
			throw new ArgumentException("The upstream must be an absolute http or https URL.", nameof(upstream));
		}

		CancellationTokenSource? superseded = null;
		string token;
		lock (_gate)
		{
			if (!_bySession.TryGetValue(sessionId, out var entry))
			{
				entry = new Entry(Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)), upstream, transport);
				_bySession[sessionId] = entry;
				_byToken[entry.Token] = entry;
			}
			else
			{
				if (!string.Equals(Origin(entry.Upstream), Origin(upstream), StringComparison.OrdinalIgnoreCase) ||
					!string.Equals(entry.Transport, transport, StringComparison.Ordinal))
				{
					superseded = entry.Abort;
					entry.Abort = new CancellationTokenSource();
				}

				entry.Upstream = upstream;
				entry.Transport = transport;
				entry.Suspended = false;
			}

			token = entry.Token;
		}

		Cancel(superseded);
		return PathPrefix + token + upstream.AbsolutePath + upstream.Query;
	}

	public void Suspend(string sessionId)
	{
		CancellationTokenSource? superseded = null;
		lock (_gate)
		{
			if (_bySession.TryGetValue(sessionId, out var entry) && !entry.Suspended)
			{
				entry.Suspended = true;
				superseded = entry.Abort;
				entry.Abort = new CancellationTokenSource();
			}
		}

		Cancel(superseded);
	}

	public void Revoke(string sessionId)
	{
		CancellationTokenSource? abort = null;
		lock (_gate)
		{
			if (_bySession.Remove(sessionId, out var entry))
			{
				_byToken.Remove(entry.Token);
				abort = entry.Abort;
			}
		}

		Cancel(abort);
	}

	public VideoStreamRelayAcquisition Acquire(string token, out VideoStreamRelayLease? lease)
	{
		lease = null;
		if (string.IsNullOrEmpty(token))
		{
			return VideoStreamRelayAcquisition.NotFound;
		}

		lock (_gate)
		{
			if (!_byToken.TryGetValue(token, out var entry) || entry.Suspended)
			{
				return VideoStreamRelayAcquisition.NotFound;
			}

			if (entry.Leases >= MaxLeasesPerSession || _leases >= MaxLeases)
			{
				return VideoStreamRelayAcquisition.Busy;
			}

			entry.Leases++;
			_leases++;
			lease = new VideoStreamRelayLease(entry.Upstream, entry.Transport, entry.Abort.Token, () => Release(entry));
			return VideoStreamRelayAcquisition.Acquired;
		}
	}

	private void Release(Entry entry)
	{
		lock (_gate)
		{
			entry.Leases--;
			_leases--;
		}
	}

	private static string Origin(Uri uri) => uri.GetLeftPart(UriPartial.Authority);

	// CancelAsync runs the callbacks off this thread, so a caller may hold its own lock.
	private static void Cancel(CancellationTokenSource? source) => _ = source?.CancelAsync();

	private sealed class Entry(string token, Uri upstream, string transport)
	{
		public string Token { get; } = token;

		public Uri Upstream { get; set; } = upstream;

		public string Transport { get; set; } = transport;

		public bool Suspended { get; set; }

		public int Leases { get; set; }

		public CancellationTokenSource Abort { get; set; } = new();
	}
}

public sealed class VideoStreamRelayLease : IDisposable
{
	private Action? _release;

	public VideoStreamRelayLease(Uri upstream, string transport, CancellationToken aborted, Action? release = null)
	{
		Upstream = upstream;
		Transport = transport;
		Aborted = aborted;
		_release = release;
	}

	public Uri Upstream { get; }

	public string Transport { get; }

	public CancellationToken Aborted { get; }

	public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
