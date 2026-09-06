using MacroDeck.Plugin.Protocol.Errors;

namespace MacroDeck.Plugin.Testing;

/// <summary>
/// How <see cref="MacroDeckTestHost" /> answers <c>POST /api/plugins/registration</c>.
/// </summary>
public sealed class PluginRegistrationPolicy
{
	private enum Kind
	{
		AcceptAny,
		RequireToken,
		Reject
	}

	private readonly Kind _kind;
	private readonly string? _token;
	private readonly string? _rejectionCode;

	private PluginRegistrationPolicy(Kind kind, string? token, string? rejectionCode)
	{
		_kind = kind;
		_token = token;
		_rejectionCode = rejectionCode;
	}

	/// <summary>Registers whatever enrollment token is presented, the same tolerance a first-time
	/// setup usually needs. The default.</summary>
	public static PluginRegistrationPolicy AcceptAny { get; } = new(Kind.AcceptAny, null, null);

	/// <summary>Registers only a plugin presenting exactly <paramref name="token" />.</summary>
	public static PluginRegistrationPolicy RequireEnrollmentToken(string token)
	{
		ArgumentException.ThrowIfNullOrEmpty(token);
		return new PluginRegistrationPolicy(Kind.RequireToken, token, null);
	}

	/// <summary>Refuses every registration attempt with <paramref name="errorCode" />, so a test can
	/// verify how a self-registering plugin behaves when it cannot obtain credentials.</summary>
	public static PluginRegistrationPolicy Reject(string errorCode)
	{
		ArgumentException.ThrowIfNullOrEmpty(errorCode);
		return new PluginRegistrationPolicy(Kind.Reject, null, errorCode);
	}

	/// <summary>Decides one registration attempt. A false result carries the error code to answer with.</summary>
	internal bool TryAuthorize(string? presentedToken, out string errorCode)
	{
		switch (_kind)
		{
			case Kind.AcceptAny:
				errorCode = string.Empty;
				return true;

			case Kind.RequireToken:
				if (string.Equals(presentedToken, _token, StringComparison.Ordinal))
				{
					errorCode = string.Empty;
					return true;
				}

				errorCode = ProtocolErrorCodes.Unauthenticated;
				return false;

			default:
				errorCode = _rejectionCode!;
				return false;
		}
	}
}
