using MacroDeck.Localization;
using MacroDeckHost.Integrations.Http.Client;
using MacroDeckHost.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.Http.Actions;

internal static class HttpFailureMessages
{
	public static (string Code, LocalizedText Message) ForFailure(HttpFailureKind failure) => failure switch
	{
		HttpFailureKind.Timeout => (ActionErrorCodes.Timeout, AppStrings.Integrations.Http.Errors.RequestTimedOut()),
		HttpFailureKind.Unreachable => (ActionErrorCodes.NotConnected,
			AppStrings.Integrations.Http.Errors.ServerUnreachable()),
		HttpFailureKind.TlsRejected =>
			(ActionErrorCodes.NotConnected, AppStrings.Integrations.Http.Errors.TlsCertificateRejected()),
		HttpFailureKind.FileMissing => (ActionErrorCodes.NotFound,
			AppStrings.Integrations.Http.Errors.UploadFileMissing()),
		HttpFailureKind.FileUnreadable => (ActionErrorCodes.ProviderError,
			AppStrings.Integrations.Http.Errors.UploadFileUnreadable()),
		_ => (ActionErrorCodes.ProviderError, AppStrings.Integrations.Http.Errors.RequestFailed())
	};

	public static (string Code, LocalizedText Message) ForStatus(int statusCode) => statusCode switch
	{
		401 or 403 => (ActionErrorCodes.PermissionDenied, AppStrings.Integrations.Http.Errors.Unauthorized()),
		404 => (ActionErrorCodes.NotFound, AppStrings.Integrations.Http.Errors.ResourceNotFound()),
		408 => (ActionErrorCodes.Timeout, AppStrings.Integrations.Http.Errors.ServerTimeout()),
		429 => (ActionErrorCodes.ProviderRejected, AppStrings.Integrations.Http.Errors.RateLimited()),
		>= 400 and < 500 => (ActionErrorCodes.ProviderRejected, AppStrings.Integrations.Http.Errors.RequestRejected()),
		>= 500 and < 600 => (ActionErrorCodes.ProviderError, AppStrings.Integrations.Http.Errors.ServerError()),
		_ => (ActionErrorCodes.ProviderRejected, AppStrings.Integrations.Http.Errors.UnexpectedStatus())
	};
}
