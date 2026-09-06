using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security;
using System.Text.Json;
using System.Text.RegularExpressions;
using MacroDeck.Localization;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Actions;

public static class ActionErrorSanitizer
{
	private const int MaxMessageLength = 200;

	public static (string Code, LocalizedText Message) Sanitize(Exception ex)
	{
		// Ordered by type hierarchy: TaskCanceledException derives from OperationCanceledException and
		// PlatformNotSupportedException from NotSupportedException, so the derived type must come first
		// or its arm is unreachable.
		return ex switch
		{
			TaskCanceledException => (ActionExecutionErrorCodes.Cancelled, AppStrings.Errors.Actions.Cancelled()),
			OperationCanceledException => (ActionExecutionErrorCodes.Cancelled, AppStrings.Errors.Actions.Cancelled()),
			TimeoutException => (ActionExecutionErrorCodes.Timeout, AppStrings.Errors.Actions.TookTooLong()),
			HttpRequestException => (ActionExecutionErrorCodes.ProviderUnreachable,
				AppStrings.Errors.Actions.ServiceUnreachable()),
			SocketException => (ActionExecutionErrorCodes.ProviderUnreachable,
				AppStrings.Errors.Actions.ServiceUnreachable()),
			WebSocketException => (ActionExecutionErrorCodes.ProviderUnreachable,
				AppStrings.Errors.Actions.ServiceUnreachable()),
			IOException => (ActionExecutionErrorCodes.ProviderUnreachable,
				AppStrings.Errors.Actions.ServiceUnreachable()),
			UnauthorizedAccessException => (ActionExecutionErrorCodes.PermissionDenied,
				AppStrings.Errors.Actions.PermissionDenied()),
			SecurityException => (ActionExecutionErrorCodes.PermissionDenied,
				AppStrings.Errors.Actions.PermissionDenied()),
			ArgumentException => (ActionExecutionErrorCodes.InvalidParameter,
				AppStrings.Errors.Actions.InvalidParameter()),
			FormatException => (ActionExecutionErrorCodes.InvalidParameter,
				AppStrings.Errors.Actions.InvalidParameter()),
			JsonException => (ActionExecutionErrorCodes.InvalidParameter,
				AppStrings.Errors.Actions.InvalidParameter()),
			PlatformNotSupportedException => (ActionExecutionErrorCodes.Unsupported,
				AppStrings.Errors.Actions.Unsupported()),
			NotSupportedException => (ActionExecutionErrorCodes.Unsupported, AppStrings.Errors.Actions.Unsupported()),
			_ => (ActionExecutionErrorCodes.ActionFailed, AppStrings.Errors.Actions.Failed())
		};
	}

	public static string ClampMessage(string message)
	{
		var collapsed = Regex.Replace(message, @"\s+", " ").Trim();
		return collapsed.Length <= MaxMessageLength ? collapsed : collapsed[..MaxMessageLength];
	}

	/// <summary>
	/// Clamps literal text the way the string overload does, and passes a localization reference through
	/// untouched: the rendered length only exists once a reader resolves it, and truncating the key would
	/// destroy the reference outright.
	/// </summary>
	public static LocalizedText ClampMessage(LocalizedText message)
		=> message.Literal is { } literal ? ClampMessage(literal) : message;
}
