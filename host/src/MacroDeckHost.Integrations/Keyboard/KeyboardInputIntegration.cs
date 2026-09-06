using MacroDeckHost.Integrations.Keyboard.Actions;
using MacroDeckHost.Integrations.Keyboard.Native;
using MacroDeckHost.Integrations.Native;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Keyboard;

[MacroDeckIntegration]
public sealed class KeyboardInputIntegration : IIntegration, IIntegrationIssueProvider, IIntegrationIconProvider
{
	public const string IntegrationId = "app.macro-deck.keyboard";

	private const string PermissionIssueId = "accessibility-permission";
	private const string UnsupportedIssueId = "platform-unsupported";
	private static readonly byte[] _icon = LoadIcon();

	private readonly KeyboardInputService _input;

	public string IconMimeType => "image/svg+xml";

	public byte[] GetIcon() => _icon;

	public KeyboardInputIntegration()
	{
		var provider = KeyboardInputProviderFactory.Create();
		var layout = new KeyboardLayoutService();
		_input = new KeyboardInputService(provider, layout);
		var sequenceExecutor =
			new KeyboardSequenceExecutor(_input, layout, IntegrationLog.For(IntegrationId));

		Actions =
		[
			new PressKeyActionDefinition(_input, layout),
			new TypeTextActionDefinition(_input),
			new RunSequenceActionDefinition(sequenceExecutor),
			new KeyDownActionDefinition(_input, layout),
			new KeyUpActionDefinition(_input, layout)
		];
	}

	public string Id => IntegrationId;
	public LocalizedText Name => AppStrings.Integrations.Keyboard.Name();
	public string Version => "1.0.0";

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public bool IsInitialized => true;

	public async Task InitializeAsync(IIntegrationContext context)
	{
		// On platforms that gate input behind an OS permission (macOS Accessibility), prompt at startup
		// when it isn't granted yet. The issue stays visible until the user grants it. The claim is what
		// keeps this and the Mouse integration from stacking two dialogs for the same single grant.
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
				Title = AppStrings.Integrations.Keyboard.Issues.UnsupportedTitle(),
				Description = AppStrings.Integrations.Keyboard.Issues.UnsupportedDescription(),
				Severity = IntegrationIssueSeverity.Error
			});
		}
		else if (_input is { RequiresPermission: true, HasPermission: false })
		{
			issues.Add(new IntegrationIssue
			{
				Id = PermissionIssueId,
				Title = AppStrings.Integrations.Keyboard.Issues.PermissionRequiredTitle(),
				Description = AppStrings.Integrations.Keyboard.Issues.PermissionRequiredDescription(),
				Severity = IntegrationIssueSeverity.Error,
				ActionLabel = AppStrings.Integrations.Keyboard.Issues.GrantPermissionAction()
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
		var assembly = typeof(KeyboardInputIntegration).Assembly;
		var name = assembly.GetManifestResourceNames()
			.First(n => n.EndsWith("keyboard-icon.svg", StringComparison.Ordinal));
		using var stream = assembly.GetManifestResourceStream(name)!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}
}
