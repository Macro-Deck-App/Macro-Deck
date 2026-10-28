using MacroDeck.Localization;
using MacroDeckHost.Application.Mcp;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Mcp;

public static class McpConfirmationPage
{
	private const string FormNonceField = "nonce";
	private const string FormDecisionField = "decision";
	private const string AllowDecision = "allow";
	private const string DeclineDecision = "decline";

	public static void MapMcpConfirmationPage(this IEndpointRouteBuilder endpoints)
	{
		endpoints.MapGet(McpPaths.Confirmation + "/{id}", Show).AllowAnonymous();
		endpoints.MapPost(McpPaths.Confirmation + "/{id}", Submit).AllowAnonymous().DisableAntiforgery();
	}

	private static async Task Show(
		string id,
		HttpContext context,
		IMcpConfirmationStore store,
		IAppPreferenceService preferences,
		ILocalizationResolver resolver)
	{
		var culture = (await preferences.GetLocalization()).Culture;
		if (store.Find(id) is not { } entry)
		{
			await WriteInvalid(context, resolver, culture);
			return;
		}

		var nonce = store.IssueFormNonce(entry);
		var title = resolver.Resolve(AppStrings.Mcp.Confirmation.PageTitle(), culture);
		var lines = string.Concat(entry.Lines.Select(line => $"<li>{McpPage.Encode(line)}</li>"));
		string[] parts =
		[
			$"<h1>{McpPage.Encode(title)}</h1>",
			$"<p>{McpPage.Encode(resolver.Resolve(AppStrings.Mcp.Confirmation.Lead(entry.ClientName), culture))}</p>",
			$"<ul>{lines}</ul>",
			$"<p>{McpPage.Encode(resolver.Resolve(AppStrings.Mcp.Confirmation.Hint(), culture))}</p>",
			$"<form method=\"post\" action=\"{McpPaths.Confirmation}/{McpPage.Encode(entry.Id)}\">",
			$"<input type=\"hidden\" name=\"{FormNonceField}\" value=\"{McpPage.Encode(nonce)}\">",
			$"<button type=\"submit\" name=\"{FormDecisionField}\" value=\"{AllowDecision}\">"
			+ $"{McpPage.Encode(resolver.Resolve(AppStrings.Mcp.Elicitation.ConfirmTitle(), culture))}</button>",
			$"<button type=\"submit\" class=\"secondary\" name=\"{FormDecisionField}\" value=\"{DeclineDecision}\">"
			+ $"{McpPage.Encode(resolver.Resolve(AppStrings.Mcp.Confirmation.DeclineAction(), culture))}</button>",
			"</form>"
		];

		await McpPage.Write(context, title, string.Concat(parts), StatusCodes.Status200OK);
	}

	private static async Task Submit(
		string id,
		HttpContext context,
		IMcpConfirmationStore store,
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
		var decision = form[FormDecisionField].ToString();
		if (decision is not (AllowDecision or DeclineDecision)
			|| !store.TryConsume(id, form[FormNonceField].ToString(), out var entry)
			|| entry is null)
		{
			await WriteInvalid(context, resolver, culture);
			return;
		}

		var allowed = decision == AllowDecision;
		store.Complete(entry, allowed);
		var heading = resolver.Resolve(allowed
				? AppStrings.Mcp.Confirmation.AllowedHeading()
				: AppStrings.Mcp.Confirmation.DeclinedHeading(),
			culture);
		await McpPage.Write(context,
			heading,
			$"<h1>{McpPage.Encode(heading)}</h1>"
			+ $"<p>{McpPage.Encode(resolver.Resolve(AppStrings.Mcp.SecretEntry.SavedMessage(entry.ClientName), culture))}</p>",
			StatusCodes.Status200OK);
	}

	private static Task WriteInvalid(HttpContext context, ILocalizationResolver resolver, string culture)
	{
		var heading = resolver.Resolve(AppStrings.Mcp.SecretEntry.InvalidHeading(), culture);
		return McpPage.Write(context,
			heading,
			$"<h1>{McpPage.Encode(heading)}</h1>"
			+ $"<p>{McpPage.Encode(resolver.Resolve(AppStrings.Mcp.Confirmation.InvalidMessage(), culture))}</p>",
			StatusCodes.Status404NotFound);
	}
}
