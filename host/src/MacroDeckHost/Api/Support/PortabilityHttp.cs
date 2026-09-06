using MacroDeckHost.Application.Portable;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Auth;
using MacroDeckHost.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Support;

public static class PortabilityHttp
{
	public const long MaxRequestBytes = 134_217_728;

	public static TransportError? RefuseUnlessDesktop(HttpContext context)
		=> LoopbackConnection.IsTrusted(context)
			? null
			: ToTransportError(PortabilityError.DesktopOnly, null);

	public static TransportError ToTransportError(PortabilityError error, string? message)
		=> new()
		{
			Code = error.ToString(),
			Message = message ?? DefaultMessage(error)
		};

	public static IActionResult ToErrorResult(ControllerBase controller, PortabilityError error, string? message)
	{
		var payload = ToTransportError(error, message);
		return error == PortabilityError.NotFound ? controller.NotFound(payload) : controller.BadRequest(payload);
	}

	private static string DefaultMessage(PortabilityError error)
		=> error switch
		{
			PortabilityError.NotFound => "Not found",
			PortabilityError.IsVirtual => "Integration-owned items cannot be exported",
			PortabilityError.InvalidArchive => "The file is not a valid Macro Deck archive",
			PortabilityError.UnsupportedVersion => "This archive was created by a newer version of Macro Deck",
			PortabilityError.PasswordRequired => "This archive is password protected",
			PortabilityError.InvalidPassword => "The password is incorrect",
			PortabilityError.WeakPassword =>
				$"The export password must be at least {PortablePasswordPolicy.MinLength} characters",
			PortabilityError.ValidationError => "The request is invalid",
			PortabilityError.DesktopOnly => "Importing from a file path is only allowed from the desktop app.",
			_ => "The operation failed"
		};
}
