using MacroDeck.Plugin.Protocol.Errors;

namespace MacroDeckHost.Application.Plugins.Capabilities;

public sealed class RemoteCapabilityException : Exception
{
	private static readonly HashSet<string> _retryableCodes = new(StringComparer.Ordinal)
	{
		ProtocolErrorCodes.Timeout,
		ProtocolErrorCodes.RateLimited,
		ProtocolErrorCodes.QueueOverflow,
		ProtocolErrorCodes.CapabilityUnavailable,
	};

	private RemoteCapabilityException(string code,
		string message,
		bool retryable,
		IReadOnlyDictionary<string, string>? details)
		: base(message)
	{
		Code = code;
		Retryable = retryable;
		Details = details;
	}

	public string Code { get; }

	public bool Retryable { get; }

	public IReadOnlyDictionary<string, string>? Details { get; }

	public static Exception From(ProtocolError error)
	{
		if (string.Equals(error.Code, ProtocolErrorCodes.Cancelled, StringComparison.Ordinal))
		{
			return new OperationCanceledException(error.Message);
		}

		return new RemoteCapabilityException(error.Code,
			error.Message,
			_retryableCodes.Contains(error.Code),
			error.Details);
	}

	public static RemoteCapabilityException CreateRetryable(string code, string message)
		=> new(code, message, retryable: true, details: null);

	public static RemoteCapabilityException CreateNonRetryable(string code, string message)
		=> new(code, message, retryable: false, details: null);
}
