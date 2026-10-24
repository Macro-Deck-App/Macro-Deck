using MacroDeck.Localization;
using MacroDeck.Sdk.Issues;
using MacroDeckHost.Integrations.AutoHotkey.Configuration;
using MacroDeckHost.Integrations.AutoHotkey.Native;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.AutoHotkey.Issues;

namespace MacroDeckHost.Integrations.AutoHotkey;

public sealed partial class AutoHotkeyIntegration : IIntegrationIssueProvider
{
	internal const string NotFoundIssueId = "autohotkey-not-found";
	internal const string NotVersion2IssueId = "autohotkey-not-v2";
	internal const string NotRunnableIssueId = "autohotkey-invalid";
	internal const string LibraryUnreadableIssueId = "library-unreadable";

	private static readonly Uri _downloadUrl = new(AutoHotkeyLinks.Download);

	public Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default)
	{
		if (!IsInitialized)
		{
			return Task.FromResult<IReadOnlyList<IntegrationIssue>>([]);
		}

		var issues = new List<IntegrationIssue>();
		if (InstallationIssue(_installation) is { } installation)
		{
			issues.Add(installation);
		}

		if (_store is { IsReadOnly: true })
		{
			issues.Add(new IntegrationIssue
			{
				Id = LibraryUnreadableIssueId,
				Title = Strings.LibraryUnreadableTitle(),
				Description = Strings.LibraryUnreadableDescription(),
				Severity = IntegrationIssueSeverity.Warning,
				ActionLabel = Strings.LibraryUnreadableAction()
			});
		}

		return Task.FromResult<IReadOnlyList<IntegrationIssue>>(issues);
	}

	public async Task<IssueResolution> ResolveIssueAsync(
		string issueId,
		CancellationToken cancellationToken = default)
	{
		if (issueId == LibraryUnreadableIssueId)
		{
			if (_store is not { } store)
			{
				return IssueResolution.Failed();
			}

			await store.LoadAsync(cancellationToken).ConfigureAwait(false);
			return store.IsReadOnly
				? IssueResolution.Failed(Strings.LibraryStillUnreadable())
				: IssueResolution.Ok(Strings.LibraryReadable());
		}

		var detected = await AutoHotkeyInstallation
			.DetectAsync(_machine, _settings.ManualExecutable, cancellationToken)
			.ConfigureAwait(false);
		await ApplyInstallationAsync(detected).ConfigureAwait(false);

		return detected.IsValid
			? IssueResolution.Ok(Strings.Found(detected.Version ?? string.Empty))
			: IssueResolution.Ok(Strings.ChooseExecutable(), IssueResolutionFollowUp.StartConfigFlow);
	}

	internal static IntegrationIssue? InstallationIssue(AutoHotkeyInstallation installation)
	{
		return installation.Status switch
		{
			AutoHotkeyInstallationStatus.NotFound => Issue(NotFoundIssueId,
				Strings.NotFoundTitle(),
				Strings.NotFoundDescription()),
			AutoHotkeyInstallationStatus.NotVersion2 => Issue(NotVersion2IssueId,
				Strings.NotVersion2Title(),
				Strings.NotVersion2Description()),
			AutoHotkeyInstallationStatus.NotRunnable => Issue(NotRunnableIssueId,
				Strings.NotRunnableTitle(),
				Strings.NotRunnableDescription()),
			_ => null
		};
	}

	private static IntegrationIssue Issue(string id, LocalizedText title, LocalizedText description)
	{
		return new IntegrationIssue
		{
			Id = id,
			Title = title,
			Description = description,
			Severity = IntegrationIssueSeverity.Error,
			ActionLabel = Strings.CheckAgain(),
			HelpUrl = _downloadUrl
		};
	}
}
