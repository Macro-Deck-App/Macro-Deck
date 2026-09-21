using MacroDeck.Plugin.Protocol.Errors;

namespace MacroDeck.Plugin.Hosting.Transport;

/// <summary>
/// What a call site sees when a <c>host.invoke</c> fails. The plugin-side mirror of the host's
/// <c>RemoteCapabilityException</c>.
/// </summary>
public sealed class HostInvocationException : Exception
{
	private static readonly HashSet<string> _retryableCodes = new(StringComparer.Ordinal)
	{
		ProtocolErrorCodes.Timeout,
		ProtocolErrorCodes.RateLimited,
		ProtocolErrorCodes.QueueOverflow,
		ProtocolErrorCodes.CapabilityUnavailable,
	};

	private HostInvocationException(string code,
		string message,
		bool retryable,
		IReadOnlyDictionary<string, string>? details = null)
		: base(message)
	{
		Code = code;
		Retryable = retryable;
		Details = details ?? new Dictionary<string, string>(StringComparer.Ordinal);
	}

	/// <summary>The wire error code, e.g. <see cref="ProtocolErrorCodes.CapabilityUnavailable"/>.</summary>
	public string Code { get; }

	public bool Retryable { get; }

	/// <summary>The error's details, empty when it carried none. A <c>reason</c> entry refines <see cref="Code" />;
	/// see <see cref="ProtocolErrorReasons" />.</summary>
	public IReadOnlyDictionary<string, string> Details { get; }

	/// <summary>Maps a wire-level <see cref="ProtocolError"/> to the exception a call site sees. A
	/// <c>CANCELLED</c> code becomes <see cref="OperationCanceledException"/>, mirroring
	/// <c>RemoteCapabilityException.From</c>.</summary>
	public static Exception From(ProtocolError error)
	{
		if (string.Equals(error.Code, ProtocolErrorCodes.Cancelled, StringComparison.Ordinal))
		{
			return new OperationCanceledException(error.Message);
		}

		return new HostInvocationException(error.Code,
			error.Message,
			_retryableCodes.Contains(error.Code),
			error.Details);
	}

	public static HostInvocationException CreateRetryable(string code, string message)
		=> new(code, message, retryable: true);

	public static HostInvocationException CreateNonRetryable(string code, string message)
		=> new(code, message, retryable: false);
}
