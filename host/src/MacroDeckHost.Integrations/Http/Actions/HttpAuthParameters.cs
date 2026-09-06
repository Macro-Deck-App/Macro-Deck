using MacroDeck.Localization;
using MacroDeckHost.Integrations.Http.Client;
using MacroDeckHost.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.Http.Actions;

internal static class HttpAuthParameters
{
	internal const string TypeParameter = "authType";
	internal const string UsernameParameter = "authUsername";
	internal const string SecretParameter = "authSecret";
	internal const string HeaderNameParameter = "authHeaderName";

	private const string None = "none";
	private const string Basic = "basic";
	private const string Bearer = "bearer";
	private const string Header = "header";

	public static IReadOnlyList<ActionParameter> Declare() =>
	[
		ActionParameter.Choice(TypeParameter,
			options:
			[
				new ActionParameterOption { Value = None },
				new ActionParameterOption { Value = Basic },
				new ActionParameterOption { Value = Bearer },
				new ActionParameterOption { Value = Header }
			],
			label: AppStrings.Integrations.Http.Auth.TypeLabel(),
			defaultValue: None),
		ActionParameter.Text(UsernameParameter, label: AppStrings.Integrations.Http.Auth.UsernameLabel())
			.OnlyWhen(TypeParameter, Basic),
		ActionParameter.Secret(SecretParameter,
				label: AppStrings.Integrations.Http.Auth.SecretLabel(),
				description: AppStrings.Integrations.Http.Auth.SecretDescription())
			.OnlyWhen(TypeParameter, Basic, Bearer, Header),
		ActionParameter.Text(HeaderNameParameter,
				label: AppStrings.Integrations.Http.Auth.HeaderNameLabel(),
				placeholder: "X-Api-Key")
			.OnlyWhen(TypeParameter, Header)
	];

	public static bool TryRead(
		IReadOnlyDictionary<string, object> parameters,
		out HttpAuth auth,
		out LocalizedText errorMessage)
	{
		var type = HttpActionValues.ReadText(parameters, TypeParameter) ?? None;
		var username = HttpActionValues.ReadText(parameters, UsernameParameter);
		var secret = HttpActionValues.ReadText(parameters, SecretParameter);
		var headerName = HttpActionValues.ReadText(parameters, HeaderNameParameter);

		switch (type.ToLowerInvariant())
		{
			case Basic:
				if (username is null && secret is null)
				{
					auth = HttpAuth.None;
					errorMessage = AppStrings.Integrations.Http.Errors.BasicAuthNeedsCredentials();
					return false;
				}

				auth = new HttpAuthBasic(username ?? string.Empty, secret ?? string.Empty);
				errorMessage = default;
				return true;

			case Bearer:
				if (secret is null)
				{
					auth = HttpAuth.None;
					errorMessage = AppStrings.Integrations.Http.Errors.BearerAuthNeedsToken();
					return false;
				}

				auth = new HttpAuthBearer(secret);
				errorMessage = default;
				return true;

			case Header:
				if (headerName is null)
				{
					auth = HttpAuth.None;
					errorMessage = AppStrings.Integrations.Http.Errors.HeaderAuthNeedsName();
					return false;
				}

				auth = new HttpAuthHeader(headerName, secret ?? string.Empty);
				errorMessage = default;
				return true;

			default:
				auth = HttpAuth.None;
				errorMessage = default;
				return true;
		}
	}
}
