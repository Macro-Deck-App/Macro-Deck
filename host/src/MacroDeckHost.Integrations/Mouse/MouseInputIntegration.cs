using MacroDeckHost.Integrations.Mouse.Actions;
using MacroDeckHost.Integrations.Mouse.Native;
using MacroDeckHost.Integrations.Native;
using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Issues;

namespace MacroDeckHost.Integrations.Mouse;

[MacroDeckIntegration]
public sealed class MouseInputIntegration : IIntegration, IIntegrationIssueProvider, IIntegrationIconProvider
{
	public const string IntegrationId = "app.macro-deck.mouse";

	private const string PermissionIssueId = "accessibility-permission";
	private const string UnsupportedIssueId = "platform-unsupported";
	private const string DegradedIssueId = "positioning-unreliable";
	private static readonly byte[] _icon = LoadIcon();

	private readonly MouseInputService _input;

	public MouseInputIntegration()
	{
		_input = new MouseInputService(MouseInputProviderFactory.Create());

		Actions =
		[
			new ClickActionDefinition(_input),
			new MoveCursorActionDefinition(_input),
			new DragActionDefinition(_input),
			new ScrollActionDefinition(_input),
			new ButtonDownActionDefinition(_input),
			new ButtonUpActionDefinition(_input),
			new ReleaseAllButtonsActionDefinition(_input)
		];
	}

	public string IconMimeType => "image/svg+xml";

	public byte[] GetIcon() => _icon;

	public string Id => IntegrationId;
	public LocalizedText Name => AppStrings.Integrations.Mouse.IntegrationName();
	public string Version => "1.0.0";

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public bool IsInitialized => true;

	public async Task InitializeAsync(IIntegrationContext context)
	{
		// On platforms that gate input behind an OS permission (macOS Accessibility), prompt at startup
		// when it isn't granted yet. This is the same single grant the Keyboard integration needs, so the
		// claim makes sure only one of the two raises a dialog.
		if (_input is { RequiresPermission: true, HasPermission: false } && MacOsAccessibility.TryClaimStartupPrompt())
		{
			await _input.RequestPermissionAsync();
		}
	}

	public Task ShutdownAsync() => _input.ReleaseAllAsync();

	public Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default)
	{
		var issues = new List<IntegrationIssue>();

		if (!_input.IsSupported)
		{
			issues.Add(new IntegrationIssue
			{
				Id = UnsupportedIssueId,
				Title = AppStrings.Integrations.Mouse.Issues.UnsupportedTitle(),
				Description = AppStrings.Integrations.Mouse.Issues.UnsupportedDescription(),
				Severity = IntegrationIssueSeverity.Error
			});
		}
		else if (_input is { RequiresPermission: true, HasPermission: false })
		{
			issues.Add(new IntegrationIssue
			{
				Id = PermissionIssueId,
				Title = AppStrings.Integrations.Mouse.Issues.PermissionRequiredTitle(),
				Description = AppStrings.Integrations.Mouse.Issues.PermissionRequiredDescription(),
				Severity = IntegrationIssueSeverity.Error,
				ActionLabel = AppStrings.Integrations.Mouse.Issues.GrantPermissionAction()
			});
		}
		else if (_input.IsDegraded)
		{
			issues.Add(new IntegrationIssue
			{
				Id = DegradedIssueId,
				Title = AppStrings.Integrations.Mouse.Issues.DegradedTitle(),
				Description = _input.DegradedReason,
				Severity = IntegrationIssueSeverity.Warning
			});
		}

		return Task.FromResult<IReadOnlyList<IntegrationIssue>>(issues);
	}

	public async Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
	{
		if (issueId != PermissionIssueId)
		{
			return IssueResolution.Failed(AppStrings.Integrations.Issues.UnknownIssue());
		}

		await _input.RequestPermissionAsync(cancellationToken);
		return _input.HasPermission
			? IssueResolution.Ok(AppStrings.Integrations.Issues.AccessibilityPermissionGranted())
			: IssueResolution.Ok(AppStrings.Integrations.Issues.ApproveAccessibilityPermission());
	}

	private static byte[] LoadIcon()
	{
		var assembly = typeof(MouseInputIntegration).Assembly;
		var name = assembly.GetManifestResourceNames()
			.First(n => n.EndsWith("mouse-icon.svg", StringComparison.Ordinal));
		using var stream = assembly.GetManifestResourceStream(name)!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}
}
