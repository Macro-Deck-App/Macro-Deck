using MacroDeck.Localization;
using MacroDeckHost.Application.Mcp;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Mcp;

public static class McpSecretEntryPage
{
	public const int MaxValueLength = 8192;
	private const string FormValueField = "value";
	private const string FormNonceField = "nonce";

	public static void MapMcpSecretEntryPage(this IEndpointRouteBuilder endpoints)
	{
		endpoints.MapGet(McpPaths.SecretEntry + "/{id}", Show).AllowAnonymous();
		endpoints.MapPost(McpPaths.SecretEntry + "/{id}", Submit).AllowAnonymous().DisableAntiforgery();
	}

	private static async Task Show(
		string id,
		HttpContext context,
		IMcpSecretEntryStore store,
		IAppPreferenceService preferences,
		ILocalizationResolver resolver)
	{
		var culture = (await preferences.GetLocalization()).Culture;
		var entry = store.Find(id);
		if (entry is null)
		{
			await WriteInvalid(context, resolver, culture);
			return;
		}

		await WriteForm(context, resolver, culture, entry, store.IssueFormNonce(entry), null);
	}

	private static async Task Submit(
		string id,
		HttpContext context,
		IMcpSecretEntryStore store,
		ISecretService secrets,
		IMcpSecretLedger ledger,
		IAppPreferenceService preferences,
		ILocalizationResolver resolver)
	{
		var culture = (await preferences.GetLocalization()).Culture;
		if (!context.Request.HasFormContentType)
		{
			await WriteInvalid(context, resolver, culture);
			return;
		}

		var form = await context.Request.ReadFormAsync(context.RequestAborted);
		var value = form[FormValueField].ToString();
		var pending = store.Find(id);
		if (pending is not null && (value.Length == 0 || value.Length > MaxValueLength))
		{
			await WriteForm(context,
				resolver,
				culture,
				pending,
				store.IssueFormNonce(pending),
				AppStrings.Mcp.SecretEntry.EmptyValue());
			return;
		}

		if (!store.TryConsume(id, form[FormNonceField].ToString(), out var entry) || entry is null)
		{
			await WriteInvalid(context, resolver, culture);
			return;
		}

		var secretId = await secrets.Create(value, entry.Kind);
		ledger.Grant(entry.TokenId, secretId);
		store.Complete(entry, secretId);

		await McpPage.Write(context,
			resolver.Resolve(AppStrings.Mcp.SecretEntry.SavedHeading(), culture),
			$"<h1>{McpPage.Encode(resolver.Resolve(AppStrings.Mcp.SecretEntry.SavedHeading(), culture))}</h1>"
			+ $"<p>{McpPage.Encode(resolver.Resolve(AppStrings.Mcp.SecretEntry.SavedMessage(entry.ClientName), culture))}</p>",
			StatusCodes.Status200OK);
	}

	private static Task WriteForm(
		HttpContext context,
		ILocalizationResolver resolver,
		string culture,
		McpSecretEntry entry,
		string nonce,
		LocalizedString? error)
	{
		var title = resolver.Resolve(AppStrings.Mcp.SecretEntry.PageTitle(), culture);
		var lead = resolver.Resolve(AppStrings.Mcp.SecretEntry.Lead(entry.ClientName, entry.Label), culture);
		var privacy = resolver.Resolve(AppStrings.Mcp.SecretEntry.Privacy(entry.ClientName), culture);
		var valueLabel = resolver.Resolve(AppStrings.Mcp.SecretEntry.ValueLabel(), culture);
		var save = resolver.Resolve(AppStrings.Mcp.SecretEntry.SaveAction(), culture);
		var errorLine = error is { } message
			? $"<p class=\"error\">{McpPage.Encode(resolver.Resolve(message, culture))}</p>"
			: string.Empty;
		string[] parts =
		[
			$"<h1>{McpPage.Encode(title)}</h1><p>{McpPage.Encode(lead)}</p><p>{McpPage.Encode(privacy)}</p>",
			$"<form method=\"post\" action=\"{McpPaths.SecretEntry}/{McpPage.Encode(entry.Id)}\" autocomplete=\"off\">",
			$"<input type=\"hidden\" name=\"{FormNonceField}\" value=\"{McpPage.Encode(nonce)}\">",
			$"<label for=\"{FormValueField}\">{McpPage.Encode(valueLabel)}</label>",
			$"<input id=\"{FormValueField}\" name=\"{FormValueField}\" type=\"password\" required autofocus>",
			errorLine,
			$"<button type=\"submit\">{McpPage.Encode(save)}</button></form>"
		];

		return McpPage.Write(context, title, string.Concat(parts), StatusCodes.Status200OK);
	}

	private static Task WriteInvalid(HttpContext context, ILocalizationResolver resolver, string culture)
	{
		var heading = resolver.Resolve(AppStrings.Mcp.SecretEntry.InvalidHeading(), culture);
		return McpPage.Write(context,
			heading,
			$"<h1>{McpPage.Encode(heading)}</h1>"
			+ $"<p>{McpPage.Encode(resolver.Resolve(AppStrings.Mcp.SecretEntry.InvalidMessage(), culture))}</p>",
			StatusCodes.Status404NotFound);
	}
}
