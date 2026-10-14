using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Application.Email;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Infrastructure.SystemIntegrations.Email;

internal sealed class RefreshInboxesActionDefinition : IActionDefinition
{
	public const string ActionId = "refresh-inboxes";

	public const string UnavailableCode = "EMAIL_REFRESH_UNAVAILABLE";

	private readonly Func<EmailHostServices?> _services;

	public RefreshInboxesActionDefinition(Func<EmailHostServices?> services)
	{
		_services = services;
	}

	public string Id => ActionId;

	public LocalizedText Name => AppStrings.Integrations.Email.Actions.RefreshInboxes.Name();

	public LocalizedText Description => AppStrings.Integrations.Email.Actions.RefreshInboxes.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } = [];

	public IActionExecutor CreateExecutor()
	{
		return new Executor(this);
	}

	private sealed class Executor : IActionExecutor
	{
		private readonly RefreshInboxesActionDefinition _definition;

		public Executor(RefreshInboxesActionDefinition definition)
		{
			_definition = definition;
		}

		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (_definition._services() is not { } services)
			{
				return Task.FromResult(ActionResult.Failed(UnavailableCode,
					AppStrings.Integrations.Email.Actions.RefreshInboxes.Unavailable()));
			}

			services.Inboxes.RequestSync();
			return Task.FromResult(ActionResult.Success());
		}
	}
}
