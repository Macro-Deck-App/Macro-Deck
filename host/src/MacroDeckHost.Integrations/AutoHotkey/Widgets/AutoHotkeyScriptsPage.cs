using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MacroDeck.Localization;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Integrations.AutoHotkey.Actions;
using MacroDeckHost.Integrations.AutoHotkey.Configuration;
using MacroDeckHost.Integrations.AutoHotkey.Native;
using MacroDeckHost.Integrations.AutoHotkey.Providers;
using MacroDeckHost.Integrations.AutoHotkey.State;
using MacroDeckHost.Integrations.Ui;
using Serilog;
using ParameterStrings = MacroDeckHost.Localization.AppStrings.Integrations.AutoHotkey.Parameters;
using StateStrings = MacroDeckHost.Localization.AppStrings.Integrations.AutoHotkey.States;
using ScriptStrings = MacroDeckHost.Localization.AppStrings.Scripts;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.AutoHotkey.Page;

namespace MacroDeckHost.Integrations.AutoHotkey.Widgets;

internal sealed record AutoHotkeyScriptRow(Guid Id, string Name, bool Running);

internal sealed record AutoHotkeyPageStatus(string Severity, LocalizedText Text, string? Detail = null);

internal sealed record AutoHotkeyPendingExport(Guid ScriptId, string Path);

internal sealed record AutoHotkeyEditTarget(Guid? ScriptId);

internal sealed class AutoHotkeyScriptsPage : IAsyncDisposable
{
	public const string ResourcePrefix = "ahk-";
	public const string NewScriptSource = "#Requires AutoHotkey v2.0\n";

	private const string TextMediaType = "text/plain";

	private static readonly UTF8Encoding _strictUtf8 = new(false, true);

	// An empty resource is rejected, and a client decoding UTF-8 drops a leading BOM, so a lone BOM is how an
	// empty script reaches the editor as empty text.
	private static readonly byte[] _emptyContent = [0xEF, 0xBB, 0xBF];
	private static readonly Uri _downloadUrl = new(AutoHotkeyLinks.Download);

	private readonly IAutoHotkeyPageHost _host;
	private readonly ILogger _logger;
	private readonly string? _culture;
	private readonly CultureInfo _formatCulture;
	private readonly SemaphoreSlim _work = new(1, 1);

	private readonly UiState<IReadOnlyList<AutoHotkeyScriptRow>> _rows = new([]);
	private readonly UiState<IReadOnlyList<Guid>> _selected = new([]);
	private readonly UiState<bool> _libraryReadOnly = new(false);
	private readonly UiState<bool> _installed = new(false);
	private readonly UiState<AutoHotkeyPageStatus?> _status = new(null);
	private readonly UiState<AutoHotkeyPageStatus?> _scriptStatus = new(null);
	private readonly UiState<UiResource?> _resource = new(null);
	private readonly UiState<bool> _sourceReadOnly = new(false);
	private readonly UiState<bool> _uploadFailed = new(false);
	private readonly UiState<bool> _dirty = new(false);
	private readonly UiState<AutoHotkeyRunSnapshot?> _run = new(null);
	private readonly UiState<string> _description = new(string.Empty);
	private readonly UiState<bool> _startWithMacroDeck = new(false);
	private readonly UiState<bool> _includeHelper = new(true);
	private readonly UiState<string> _workingDirectory = new(string.Empty);
	private readonly UiState<string> _importPath = new(string.Empty);
	private readonly UiState<string> _exportFolder = new(string.Empty);
	private readonly UiState<AutoHotkeyPendingExport?> _pendingOverwrite = new(null);
	private readonly UiState<Guid?> _exportTarget = new(null);
	private readonly UiState<bool> _importOpen = new(false);
	private readonly UiState<AutoHotkeyPageStatus?> _importStatus = new(null);
	private readonly UiState<AutoHotkeyPageStatus?> _exportStatus = new(null);
	private readonly UiState<AutoHotkeyEditTarget?> _editing = new(null);
	private readonly UiState<string> _editName = new(string.Empty);
	private readonly UiState<string> _editDescription = new(string.Empty);
	private readonly UiState<AutoHotkeyPageStatus?> _editStatus = new(null);

	private AutoHotkeyScriptStore _store;
	private AutoHotkeyProcessManager _runs;
	private IUiResourceRegistry _resources;
	private string _savedSource = string.Empty;
	private string _draft = string.Empty;
	private byte[]? _draftBytes;
	private string? _resourceName;
	private bool _disposed;

	public AutoHotkeyScriptsPage(IAutoHotkeyPageHost host, string? culture, ILogger logger)
	{
		_host = host;
		_culture = culture;
		_formatCulture = FormatCulture(culture);
		_logger = logger;
		_store = host.Library ?? throw new InvalidOperationException("The AutoHotkey integration is not running.");
		_runs = host.Runs ?? throw new InvalidOperationException("The AutoHotkey integration is not running.");
		_resources = host.Resources
			?? throw new InvalidOperationException("The AutoHotkey integration is not running.");
		Refresh();
		_store.Changed += OnLibraryChanged;
		_runs.RunChanged += OnRunChanged;
		_host.RuntimeChanged += OnRuntimeChanged;
	}

	public UiViewSession? Session { get; set; }

	internal Guid? SelectedId => _selected.Value is [var id] ? id : null;

	internal string Draft => _draft;

	internal string? ResourceName => _resourceName;

	public UiElement Build()
	{
		return new UiConfigStack
		{
			Key = "autohotkey-scripts",
			Children =
			[
				new UiWhen
				{
					Key = "not-installed-when",
					Condition = () => !_installed.Value,
					Content = NotInstalled
				},
				new UiWhen
				{
					Key = "read-only-when",
					Condition = () => _libraryReadOnly.Value,
					Content = () => new UiBanner
					{
						Key = "read-only", Severity = "warning", Text = Strings.LibraryReadOnlyBanner()
					}
				},
				new UiWhen
				{
					Key = "status-when",
					Condition = () => _status.Value is not null,
					Content = () => StatusBanner("status", _status)
				},
				new UiSplit
				{
					Key = "scripts",
					RailTitle = Strings.Title(),
					RailActions =
					[
						new UiWhen
						{
							Key = "rail-actions-when",
							Condition = () => !_libraryReadOnly.Value,
							Content = RailActions
						}
					],
					Rail =
					[
						new UiConfigList
						{
							Key = "script-list",
							Label = Strings.Title(),
							Children =
							[
								new UiRepeat<AutoHotkeyScriptRow>
								{
									Key = "script-items",
									Items = UiValue.From(() => _rows.Value),
									KeySelector = row => "row-" + row.Id.ToString("N"),
									Template = (row, key) => Row(row, key)
								}
							]
						}
					],
					Detail =
					[
						new UiRepeat<Guid>
						{
							Key = "editor",
							Items = UiValue.From(() => _selected.Value),
							KeySelector = id => "editor-" + id.ToString("N"),
							Template = (id, key) => Editor(id, key)
						},
						new UiWhen
						{
							Key = "empty-when",
							Condition = () => _selected.Value.Count == 0,
							Content = EmptyState
						}
					]
				},
				new UiWhen
				{
					Key = "edit-when",
					Condition = () => _editing.Value is not null,
					Content = EditorDialog
				},
				new UiWhen
				{
					Key = "import-when",
					Condition = () => _importOpen.Value,
					Content = ImportDialog
				},
				new UiWhen
				{
					Key = "export-when",
					Condition = () => _exportTarget.Value is not null,
					Content = ExportDialog
				},
				new UiWhen
				{
					Key = "overwrite-when",
					Condition = () => _pendingOverwrite.Value is not null,
					Content = OverwriteDialog
				}
			]
		};
	}

	public async ValueTask DisposeAsync()
	{
		_disposed = true;
		_host.RuntimeChanged -= OnRuntimeChanged;
		_store.Changed -= OnLibraryChanged;
		_runs.RunChanged -= OnRunChanged;
		await _work.WaitAsync().ConfigureAwait(false);
		try
		{
			await RemoveResourceAsync().ConfigureAwait(false);
		}
		finally
		{
			_work.Release();
		}
	}

	internal async Task SelectAsync(Guid id, CancellationToken cancellationToken)
	{
		await RunExclusiveAsync(() =>
			{
				_status.Value = null;
				_scriptStatus.Value = null;
				_pendingOverwrite.Value = null;
				return SelectLockedAsync(id, cancellationToken);
			})
			.ConfigureAwait(false);
	}

	private UiConfigStack NotInstalled()
	{
		return new UiConfigStack
		{
			Key = "not-installed",
			Children =
			[
				new UiBanner
				{
					Key = "not-installed-banner", Severity = "warning", Text = Strings.NotInstalledBanner()
				},
				new UiConfigButton
				{
					Key = "download",
					Label = Strings.DownloadButton(),
					Events = [UiEventHandler.OnAsync(UiConfigEvents.Activate, OpenDownloadAsync)]
				}
			]
		};
	}

	private static UiConfigStack StatusBanner(string key, UiState<AutoHotkeyPageStatus?> status)
	{
		return new UiConfigStack
		{
			Key = key,
			Children =
			[
				new UiBanner
				{
					Key = key + "-banner",
					Severity = UiValue.From(() => status.Value?.Severity ?? "info"),
					Text = UiText.Optional(() => status.Value?.Text ?? default(LocalizedText))
				},
				new UiWhen
				{
					Key = key + "-detail-when",
					Condition = () => status.Value?.Detail is { Length: > 0 },
					Content = () => new UiProse
					{
						Key = key + "-detail",
						Text = UiText.From(() => status.Value?.Detail),
						Variant = "code-block"
					}
				}
			]
		};
	}

	private UiConfigStack RailActions()
	{
		return new UiConfigStack
		{
			Key = "rail-buttons",
			Direction = "horizontal",
			Gap = UiStackGaps.Small,
			Children = [NewButton("new"), ImportButton()]
		};
	}

	private UiConfigButton NewButton(string key)
	{
		return new UiConfigButton
		{
			Key = key,
			Label = ScriptStrings.NewScript(),
			Icon = "plus",
			Variant = UiButtonVariants.Secondary,
			Events = [UiEventHandler.On(UiConfigEvents.Activate, () => OpenEditor(null))]
		};
	}

	private UiConfigButton ImportButton()
	{
		return new UiConfigButton
		{
			Key = "import-open",
			Label = Strings.ImportButton(),
			Icon = "upload",
			Variant = UiButtonVariants.Secondary,
			Events = [UiEventHandler.On(UiConfigEvents.Activate, () => SetImportOpen(true))]
		};
	}

	private UiConfigListItem Row(AutoHotkeyScriptRow row, string key)
	{
		return new UiConfigListItem
		{
			Key = key,
			Label = row.Name,
			Badge = row.Running ? StateStrings.Running() : UiText.None(),
			Severity = row.Running ? UiValue.Of("success") : UiValue.None<string>(),
			Selected = UiValue.From(() => SelectedId == row.Id),
			Events =
			[
				UiEventHandler.OnAsync(UiConfigEvents.Activate,
					cancellationToken => SelectAsync(row.Id, cancellationToken))
			]
		};
	}

	private UiEmptyState EmptyState()
	{
		return new UiEmptyState
		{
			Key = "empty",
			Icon = "code",
			Title = UiText.FromLocalized(()
				=> _rows.Value.Count == 0 ? Strings.NoScriptsTitle() : Strings.NoSelectionTitle()),
			Text = UiText.FromLocalized(()
				=> _rows.Value.Count == 0 ? Strings.NoScriptsText() : Strings.NoSelectionText()),
			Children =
			[
				new UiWhen
				{
					Key = "empty-new-when",
					Condition = () => _rows.Value.Count == 0 && !_libraryReadOnly.Value,
					Content = () => NewButton("empty-new") with
					{
						Variant = UiButtonVariants.Primary, IconWithLabel = true
					}
				}
			]
		};
	}

	private UiConfigStack ScriptButtons(Guid id)
	{
		return new UiConfigStack
		{
			Key = "script-buttons",
			Direction = "horizontal",
			Wrap = true,
			Gap = UiStackGaps.Small,
			Children =
			[
				new UiConfigButton
				{
					Key = "run",
					Label = ScriptStrings.RunAction(),
					Icon = "play",
					IconWithLabel = true,
					Variant = UiButtonVariants.Secondary,
					Events =
					[
						UiEventHandler.OnAsync(UiConfigEvents.Activate,
							cancellationToken => RunAsync(id, cancellationToken))
					]
				},
				new UiConfigButton
				{
					Key = "stop",
					Label = Strings.StopButton(),
					Variant = UiButtonVariants.Secondary,
					Events =
					[
						UiEventHandler.OnAsync(UiConfigEvents.Activate,
							cancellationToken => _runs.StopScriptAsync(id, cancellationToken))
					]
				},
				new UiWhen
				{
					Key = "library-buttons-when",
					Condition = () => !_libraryReadOnly.Value,
					Content = () => LibraryButtons(id)
				}
			]
		};
	}

	private UiFragment LibraryButtons(Guid id)
	{
		return new UiFragment
		{
			Key = "library-buttons",
			Children =
			[
				new UiConfigButton
				{
					Key = "rename",
					Label = MacroDeckStrings.Common.Rename(),
					Variant = UiButtonVariants.Secondary,
					Events = [UiEventHandler.On(UiConfigEvents.Activate, () => OpenEditor(id))]
				},
				new UiConfigButton
				{
					Key = "duplicate",
					Label = ScriptStrings.DuplicateAction(),
					Variant = UiButtonVariants.Secondary,
					Events =
					[
						UiEventHandler.OnAsync(UiConfigEvents.Activate,
							cancellationToken => DuplicateAsync(id, cancellationToken))
					]
				},
				new UiConfigButton
				{
					Key = "export",
					Label = Strings.ExportButton(),
					Variant = UiButtonVariants.Secondary,
					Events = [UiEventHandler.On(UiConfigEvents.Activate, () => SetExportTarget(id))]
				},
				new UiConfigButton
				{
					Key = "delete",
					Label = MacroDeckStrings.Common.Delete(),
					Variant = UiButtonVariants.Danger,
					ConfirmTitle = Strings.DeleteTitle(),
					ConfirmMessage = UiText.FromLocalized(()
						=> Strings.DeleteMessage(_store.Find(id)?.Name ?? string.Empty)),
					ConfirmLabel = MacroDeckStrings.Common.Delete(),
					ConfirmDanger = true,
					Events =
					[
						UiEventHandler.OnAsync(UiConfigEvents.Activate,
							cancellationToken => DeleteAsync(id, cancellationToken))
					]
				}
			]
		};
	}

	private UiConfigDialog EditorDialog()
	{
		return new UiConfigDialog
		{
			Key = "edit-dialog",
			Title = UiText.FromLocalized(() => _editing.Value is { ScriptId: null }
				? ScriptStrings.NewScriptHeading()
				: ScriptStrings.RenameScriptHeading()),
			Events = [UiEventHandler.On(UiConfigEvents.Cancel, CloseEditor)],
			Children =
			[
				new UiWhen
				{
					Key = "edit-status-when",
					Condition = () => _editStatus.Value is not null,
					Content = () => StatusBanner("edit-status", _editStatus)
				},
				new UiStringInput
				{
					Key = "edit-name",
					LiteralOnly = true,
					Transient = true,
					MaxLength = AutoHotkeyScriptStore.MaxNameLength,
					Label = ScriptStrings.NameFieldLabel(),
					Placeholder = ScriptStrings.NameFieldPlaceholder(),
					Binding = Bind.To(_editName)
				},
				new UiStringInput
				{
					Key = "edit-description",
					LiteralOnly = true,
					Transient = true,
					MaxLength = 500,
					Label = ScriptStrings.DescriptionFieldLabel(),
					Placeholder = ScriptStrings.DescriptionFieldPlaceholder(),
					Binding = Bind.To(_editDescription)
				},
				new UiConfigButton
				{
					Key = "edit-cancel",
					Label = MacroDeckStrings.Common.Cancel(),
					Events = [UiEventHandler.On(UiConfigEvents.Activate, CloseEditor)]
				},
				new UiConfigButton
				{
					Key = "edit-save",
					Label = UiText.FromLocalized(() => _editing.Value is { ScriptId: null }
						? ScriptStrings.CreateScriptAction()
						: ScriptStrings.SaveChangesAction()),
					Variant = UiButtonVariants.Primary,
					Events = [UiEventHandler.OnAsync(UiConfigEvents.Activate, SubmitEditorAsync)]
				}
			]
		};
	}

	private UiConfigStack Editor(Guid id, string key)
	{
		var readOnly = UiValue.From(() => _libraryReadOnly.Value || _sourceReadOnly.Value);
		return new UiConfigStack
		{
			Key = key,
			Children =
			[
				new UiHeading
				{
					Key = "name",
					Text = UiText.From(() => _rows.Value.FirstOrDefault(row => row.Id == id)?.Name ?? string.Empty),
					Badge = UiText.Optional(() => _dirty.Value ? ScriptStrings.UnsavedTag() : UiText.None())
				},
				new UiWhen
				{
					Key = "description-when",
					Condition = () => _description.Value.Length > 0,
					Content = () => new UiProse
					{
						Key = "description",
						Text = UiText.From(() => _description.Value),
						Variant = "subtext"
					}
				},
				ScriptButtons(id),
				new UiWhen
				{
					Key = "source-read-only-when",
					Condition = () => _sourceReadOnly.Value,
					Content = () => new UiBanner
					{
						Key = "source-read-only", Severity = "warning", Text = Strings.SourceReadOnlyBanner()
					}
				},
				new UiWhen
				{
					Key = "upload-failed-when",
					Condition = () => _uploadFailed.Value,
					Content = () => new UiBanner
					{
						Key = "upload-failed", Severity = "error", Text = Strings.UploadFailed()
					}
				},
				new UiCodeInput
				{
					Key = "source",
					Label = Strings.EditorLabel(),
					HideLabel = true,
					Language = UiCodeLanguages.AutoHotkey,
					Rows = 22,
					VariableInsertFormat = AutoHotkeyParameters.VariableInsertFormat,
					ContentResource = UiValue.Optional(() => _resource.Value is { } resource
						? UiValue.Of(resource)
						: UiValue.None<UiResource>()),
					Disabled = readOnly,
					Transient = true,
					Events =
					[
						UiCodeInput.OnContentUploaded((upload, cancellationToken)
							=> OnUploadedAsync(id, upload, cancellationToken)),
						UiCodeInput.OnContentUploadFailed(() => _uploadFailed.Value = true)
					]
				},
				new UiWhen
				{
					Key = "script-status-when",
					Condition = () => _scriptStatus.Value is not null,
					Content = () => StatusBanner("script-status", _scriptStatus)
				},
				Settings(readOnly),
				RunPanel(),
				Footer(id)
			]
		};
	}

	private UiConfigStack Footer(Guid id)
	{
		return new UiConfigStack
		{
			Key = "footer",
			Direction = "horizontal",
			Align = UiStackAlignments.End,
			Gap = UiStackGaps.Small,
			Children =
			[
				new UiConfigButton
				{
					Key = "check",
					Label = Strings.CheckSyntaxButton(),
					Icon = "check",
					IconWithLabel = true,
					Events = [UiEventHandler.OnAsync(UiConfigEvents.Activate, CheckSyntaxAsync)]
				},
				new UiConfigButton
				{
					Key = "discard",
					Label = Strings.DiscardButton(),
					Events =
					[
						UiEventHandler.OnAsync(UiConfigEvents.Activate,
							cancellationToken => DiscardAsync(id, cancellationToken))
					]
				},
				new UiWhen
				{
					Key = "save-when",
					Condition = () => !_uploadFailed.Value && !_sourceReadOnly.Value && !_libraryReadOnly.Value,
					Content = () => new UiConfigButton
					{
						Key = "save",
						Label = Strings.SaveButton(),
						Variant = UiButtonVariants.Primary,
						Events =
						[
							UiEventHandler.OnAsync(UiConfigEvents.Activate,
								cancellationToken => SaveAsync(id, cancellationToken))
						]
					}
				}
			]
		};
	}

	private UiConfigDialog ImportDialog()
	{
		return new UiConfigDialog
		{
			Key = "import-dialog",
			Title = Strings.ImportFileLabel(),
			Text = Strings.ImportNote(),
			Events = [UiEventHandler.On(UiConfigEvents.Cancel, () => SetImportOpen(false))],
			Children =
			[
				new UiWhen
				{
					Key = "import-status-when",
					Condition = () => _importStatus.Value is not null,
					Content = () => StatusBanner("import-status", _importStatus)
				},
				new UiFileInput
				{
					Key = "import-file",
					LiteralOnly = true,
					Label = Strings.ImportFileLabel(),
					HideLabel = true,
					FileExtensions = UiValue.Of<IReadOnlyList<string>>(["ahk"]),
					Transient = true,
					Binding = Bind.To(_importPath)
				},
				new UiConfigButton
				{
					Key = "import-cancel",
					Label = MacroDeckStrings.Common.Cancel(),
					Events = [UiEventHandler.On(UiConfigEvents.Activate, () => SetImportOpen(false))]
				},
				new UiConfigButton
				{
					Key = "import",
					Label = Strings.ImportButton(),
					Events = [UiEventHandler.OnAsync(UiConfigEvents.Activate, ImportAsync)]
				}
			]
		};
	}

	private UiConfigDialog ExportDialog()
	{
		return new UiConfigDialog
		{
			Key = "export-dialog",
			Title = Strings.ExportFolderLabel(),
			Events = [UiEventHandler.On(UiConfigEvents.Cancel, () => SetExportTarget(null))],
			Children =
			[
				new UiWhen
				{
					Key = "export-status-when",
					Condition = () => _exportStatus.Value is not null,
					Content = () => StatusBanner("export-status", _exportStatus)
				},
				new UiFolderInput
				{
					Key = "export-folder",
					LiteralOnly = true,
					Label = Strings.ExportFolderLabel(),
					HideLabel = true,
					Transient = true,
					Binding = Bind.To(_exportFolder)
				},
				new UiConfigButton
				{
					Key = "export-cancel",
					Label = MacroDeckStrings.Common.Cancel(),
					Events = [UiEventHandler.On(UiConfigEvents.Activate, () => SetExportTarget(null))]
				},
				new UiConfigButton
				{
					Key = "export-confirm",
					Label = Strings.ExportButton(),
					Events =
					[
						UiEventHandler.OnAsync(UiConfigEvents.Activate,
							cancellationToken => _exportTarget.Value is { } id
								? ExportAsync(id, false, cancellationToken)
								: Task.CompletedTask)
					]
				}
			]
		};
	}

	private UiConfigDialog OverwriteDialog()
	{
		return new UiConfigDialog
		{
			Key = "overwrite",
			Title = Strings.ExportExistsTitle(),
			Text = UiText.FromLocalized(() => Strings.ExportExistsMessage(
				Path.GetFileName(_pendingOverwrite.Value?.Path ?? string.Empty))),
			Events = [UiEventHandler.On(UiConfigEvents.Cancel, () => _pendingOverwrite.Value = null)],
			Children =
			[
				new UiConfigButton
				{
					Key = "overwrite-cancel",
					Label = MacroDeckStrings.Common.Cancel(),
					Events = [UiEventHandler.On(UiConfigEvents.Activate, () => _pendingOverwrite.Value = null)]
				},
				new UiConfigButton
				{
					Key = "overwrite-confirm",
					Label = Strings.ReplaceButton(),
					ConfirmDanger = true,
					Events =
					[
						UiEventHandler.OnAsync(UiConfigEvents.Activate,
							cancellationToken => _pendingOverwrite.Value is { } pending
								? ExportAsync(pending.ScriptId, true, cancellationToken)
								: Task.CompletedTask)
					]
				}
			]
		};
	}

	private UiConfigStack Settings(UiValue<bool> readOnly)
	{
		return new UiConfigStack
		{
			Key = "settings",
			Variant = UiStackVariants.Card,
			Children =
			[
				new UiHeading { Key = "settings-heading", Text = Strings.SettingsHeading() },
				new UiBooleanInput
				{
					Key = "start-with-macro-deck",
					Label = Strings.StartWithMacroDeckLabel(),
					Description = Strings.StartWithMacroDeckDescription(),
					Transient = true,
					Disabled = readOnly,
					Binding = Bind.Custom(() => _startWithMacroDeck.Value,
						value => SetSetting(_startWithMacroDeck, value))
				},
				new UiBooleanInput
				{
					Key = "include-helper",
					Label = ParameterStrings.IncludeHelperLabel(),
					Description = ParameterStrings.IncludeHelperDescription(),
					Transient = true,
					Disabled = readOnly,
					Binding = Bind.Custom(() => _includeHelper.Value, value => SetSetting(_includeHelper, value))
				},
				new UiFolderInput
				{
					Key = "working-directory",
					LiteralOnly = true,
					Label = ParameterStrings.WorkingDirectoryLabel(),
					Description = Strings.WorkingDirectoryDescription(),
					Transient = true,
					Disabled = readOnly,
					Binding = Bind.Custom(() => _workingDirectory.Value,
						value => SetSetting(_workingDirectory, value))
				}
			]
		};
	}

	private UiConfigStack RunPanel()
	{
		return new UiConfigStack
		{
			Key = "run-panel",
			Variant = UiStackVariants.Card,
			Children =
			[
				new UiHeading { Key = "run-heading", Text = Strings.RunHeading() },
				new UiStatus
				{
					Key = "run-status",
					Label = Strings.RunStatusLabel(),
					Value = UiText.FromLocalized(() => RunState(_run.Value))
				},
				new UiWhen
				{
					Key = "run-duration-when",
					Condition = () => _run.Value?.Duration is not null,
					Content = () => new UiStatus
					{
						Key = "run-duration",
						Label = Strings.DurationLabel(),
						Value = UiText.FromLocalized(() => Strings.Seconds(Seconds(_run.Value?.Duration)))
					}
				},
				new UiWhen
				{
					Key = "run-error-when",
					Condition = () => _run.Value?.Error is not null,
					Content = () => new UiBanner
					{
						Key = "run-error",
						Severity = "error",
						Text = UiText.FromLocalized(() => ErrorText(_run.Value?.Error))
					}
				},
				new UiWhen
				{
					Key = "run-result-when",
					Condition = () => _run.Value?.Result is { Length: > 0 },
					Content = () => new UiStatus
					{
						Key = "run-result",
						Label = Strings.ResultLabel(),
						Value = UiText.From(() => _run.Value?.Result)
					}
				},
				new UiWhen
				{
					Key = "run-output-when",
					Condition = () => _run.Value?.Output is { Length: > 0 },
					Content = () => new UiProse
					{
						Key = "run-output",
						Text = UiText.From(() => _run.Value?.Output),
						Variant = "code-block"
					}
				}
			]
		};
	}

	private async Task OpenDownloadAsync(CancellationToken cancellationToken)
	{
		if (Session?.Origin is { } origin)
		{
			await origin.OpenUrlAsync(_downloadUrl, cancellationToken).ConfigureAwait(false);
		}
	}

	private Task ImportAsync(CancellationToken cancellationToken)
	{
		return RunExclusiveAsync(async () =>
			{
				var path = _importPath.Value.Trim().Trim('"');
				if (path.Length == 0 || !Path.IsPathFullyQualified(path) || !File.Exists(path))
				{
					Visible(_importStatus).Value = new AutoHotkeyPageStatus("error", Strings.ChooseImportFile());
					return;
				}

				if (new FileInfo(path).Length > AutoHotkeyScriptStore.MaxSourceBytes)
				{
					Visible(_importStatus).Value = new AutoHotkeyPageStatus("error", Strings.ImportTooLarge());
					return;
				}

				var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
				var created = await _store.ImportAsync(Path.GetFileName(path),
						bytes,
						Name(Strings.ImportedName()),
						cancellationToken)
					.ConfigureAwait(false);
				_importPath.Value = string.Empty;
				SetImportOpen(false);
				await SelectLockedAsync(created.Id, cancellationToken).ConfigureAwait(false);
				_status.Value = new AutoHotkeyPageStatus("success", Strings.Imported(created.Name));
			},
			_importStatus);
	}

	private Task DuplicateAsync(Guid id, CancellationToken cancellationToken)
	{
		return RunExclusiveAsync(async () =>
		{
			var original = _store.Find(id);
			if (original is null)
			{
				return;
			}

			var name = Name(Strings.CopyName(original.Name));
			if (name.Length > AutoHotkeyScriptStore.MaxNameLength)
			{
				name = name[..AutoHotkeyScriptStore.MaxNameLength].Trim();
			}

			var copy = await _store.DuplicateAsync(id, name, cancellationToken).ConfigureAwait(false);
			await SelectLockedAsync(copy.Id, cancellationToken).ConfigureAwait(false);
		});
	}

	private Task DeleteAsync(Guid id, CancellationToken cancellationToken)
	{
		return RunExclusiveAsync(async () =>
		{
			await _store.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
			if (SelectedId == id)
			{
				await ClearSelectionAsync().ConfigureAwait(false);
			}
		});
	}

	private Task SaveAsync(Guid id, CancellationToken cancellationToken)
	{
		return RunExclusiveAsync(async () =>
			{
				if (SelectedId != id
					|| _uploadFailed.Value
					|| _sourceReadOnly.Value
					|| _store.Find(id) is not { } script)
				{
					return;
				}

				var settings = new AutoHotkeyScriptSettings(_description.Value,
					_startWithMacroDeck.Value,
					_includeHelper.Value,
					_workingDirectory.Value);
				if (settings != SettingsOf(script))
				{
					await _store.UpdateSettingsAsync(id, settings, cancellationToken).ConfigureAwait(false);
				}

				if (!string.Equals(_draft, _savedSource, StringComparison.Ordinal))
				{
					await _store.SaveSourceAsync(id, _draft, cancellationToken).ConfigureAwait(false);
					_savedSource = _draft;

					// The client keeps its editor, cursor and undo history only when the new resource holds exactly
					// the bytes it uploaded.
					await PublishAsync(_draftBytes ?? Encoding.UTF8.GetBytes(_draft), cancellationToken)
						.ConfigureAwait(false);
				}

				_dirty.Value = false;
				_scriptStatus.Value = new AutoHotkeyPageStatus("success", Strings.Saved());
			},
			_scriptStatus);
	}

	private Task DiscardAsync(Guid id, CancellationToken cancellationToken)
	{
		return RunExclusiveAsync(async () =>
			{
				if (SelectedId != id || _store.Find(id) is not { } script)
				{
					return;
				}

				LoadSettings(script);
				_draft = _savedSource;
				_draftBytes = null;
				_uploadFailed.Value = false;
				_dirty.Value = false;
				_scriptStatus.Value = null;
				await PublishAsync(Encoding.UTF8.GetBytes(_savedSource), cancellationToken).ConfigureAwait(false);
			},
			_scriptStatus);
	}

	private Task CheckSyntaxAsync(CancellationToken cancellationToken)
	{
		return RunExclusiveAsync(async () =>
			{
				var result = await _runs.CheckSyntaxAsync(_draft, _includeHelper.Value, cancellationToken)
					.ConfigureAwait(false);
				_scriptStatus.Value = result switch
				{
					{ IsValid: true } => new AutoHotkeyPageStatus("success", Strings.SyntaxOk()),
					{ Line: { } line } => new AutoHotkeyPageStatus("error", Strings.SyntaxError(line), result.Message),
					_ => new AutoHotkeyPageStatus("error", Strings.SyntaxErrorNoLine(), result.Message)
				};
			},
			_scriptStatus);
	}

	private Task RunAsync(Guid id, CancellationToken cancellationToken)
	{
		return RunExclusiveAsync(async () =>
			{
				if (SelectedId != id)
				{
					return;
				}

				var result = await _runs.RunFromEditorAsync(id, _draft, cancellationToken).ConfigureAwait(false);
				_scriptStatus.Value = result.Status == AutoHotkeyRunStatus.Failed
					? new AutoHotkeyPageStatus("error", result.ErrorMessage)
					: null;
				_run.Value = _runs.LatestRun(id);
			},
			_scriptStatus);
	}

	private Task ExportAsync(Guid id, bool overwrite, CancellationToken cancellationToken)
	{
		var failures = overwrite ? _status : _exportStatus;
		return RunExclusiveAsync(async () =>
			{
				var folder = _exportFolder.Value.Trim().Trim('"');
				_pendingOverwrite.Value = null;
				if (folder.Length == 0 || !Path.IsPathFullyQualified(folder) || !Directory.Exists(folder))
				{
					Visible(failures).Value = new AutoHotkeyPageStatus("error", Strings.ChooseExportFolder());
					return;
				}

				var result = await _store.ExportAsync(id, folder, overwrite, cancellationToken).ConfigureAwait(false);
				SetExportTarget(null);
				if (result.Status == AutoHotkeyExportStatus.AlreadyExists)
				{
					_pendingOverwrite.Value = new AutoHotkeyPendingExport(id, result.FilePath);
					return;
				}

				_status.Value
					= new AutoHotkeyPageStatus("success", Strings.Exported(Path.GetFileName(result.FilePath)));
			},
			failures);
	}

	private async Task OnUploadedAsync(Guid id, UiContentUpload upload, CancellationToken cancellationToken)
	{
		// The origin belongs to the event being dispatched, so it is read before the first await.
		if (Session?.Origin is not { } origin)
		{
			return;
		}

		ReadOnlyMemory<byte> bytes;
		try
		{
			bytes = await origin.ReadUploadAsync(upload.UploadId, cancellationToken).ConfigureAwait(false);
		}
		catch (UiUploadNotFoundException)
		{
			return;
		}
		catch (NotSupportedException ex)
		{
			_logger.Warning(ex, "This Macro Deck cannot read the uploaded AutoHotkey script");
			return;
		}

		await RunExclusiveAsync(() =>
			{
				if (SelectedId != id)
				{
					return Task.CompletedTask;
				}

				try
				{
					_draft = _strictUtf8.GetString(bytes.Span);
				}
				catch (DecoderFallbackException)
				{
					_uploadFailed.Value = true;
					return Task.CompletedTask;
				}

				_draftBytes = bytes.ToArray();
				_uploadFailed.Value = false;
				_dirty.Value = IsDirty();
				return Task.CompletedTask;
			})
			.ConfigureAwait(false);
	}

	private async Task SelectLockedAsync(Guid id, CancellationToken cancellationToken)
	{
		if (_store.Find(id) is not { } script)
		{
			return;
		}

		_scriptStatus.Value = null;
		_uploadFailed.Value = false;
		_dirty.Value = false;
		LoadSettings(script);
		_run.Value = _runs.LatestRun(id);
		var source = await _store.ReadSourceAsync(id, cancellationToken).ConfigureAwait(false);
		_sourceReadOnly.Value = !source.IsWritable;
		_savedSource = source.Text;
		_draft = source.Text;
		_draftBytes = null;
		_selected.Value = [id];
		await PublishAsync(Encoding.UTF8.GetBytes(source.Text), cancellationToken).ConfigureAwait(false);
	}

	private async Task ClearSelectionAsync()
	{
		_selected.Value = [];
		_run.Value = null;
		_savedSource = string.Empty;
		_draft = string.Empty;
		_draftBytes = null;
		await RemoveResourceAsync().ConfigureAwait(false);
	}

	// A resource is readable by any client that knows its id, so every registration gets a fresh name with
	// 128 random bits and the previous one is removed.
	private async Task PublishAsync(byte[] content, CancellationToken cancellationToken)
	{
		var name = ResourcePrefix + RandomNumberGenerator.GetHexString(32, true);
		var resource = await _resources.RegisterAsync(name,
				content.Length == 0 ? _emptyContent : content,
				TextMediaType,
				cancellationToken)
			.ConfigureAwait(false);
		var previous = _resourceName;
		_resourceName = name;
		_resource.Value = resource;
		if (previous is not null)
		{
			await RemoveQuietlyAsync(_resources, previous).ConfigureAwait(false);
		}
	}

	private async Task RemoveResourceAsync()
	{
		var previous = _resourceName;
		_resourceName = null;
		_resource.Value = null;
		if (previous is not null)
		{
			await RemoveQuietlyAsync(_resources, previous).ConfigureAwait(false);
		}
	}

	private async Task RemoveQuietlyAsync(IUiResourceRegistry resources, string name)
	{
		try
		{
			await resources.RemoveAsync(name).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is UiResourceException
			or ArgumentException
			or ObjectDisposedException
			or InvalidOperationException)
		{
			_logger.Debug(ex, "An AutoHotkey script resource could not be removed");
		}
	}

	// Saving the settings re-initialises the integration with a new library, process manager and resource
	// registry while this page stays open, so the page moves over to them instead of going stale.
	private void OnRuntimeChanged()
	{
		if (_host.Library is { } store
			&& _host.Runs is { } runs
			&& _host.Resources is { } resources
			&& (!ReferenceEquals(store, _store) || !ReferenceEquals(runs, _runs)))
		{
			_ = RunExclusiveAsync(() => RebindAsync(store, runs, resources));
		}
		else
		{
			Refresh();
		}
	}

	private async Task RebindAsync(
		AutoHotkeyScriptStore store,
		AutoHotkeyProcessManager runs,
		IUiResourceRegistry resources)
	{
		_store.Changed -= OnLibraryChanged;
		_runs.RunChanged -= OnRunChanged;
		var previousResources = _resources;
		var previousName = _resourceName;
		_store = store;
		_runs = runs;
		_resources = resources;
		_resourceName = null;
		_store.Changed += OnLibraryChanged;
		_runs.RunChanged += OnRunChanged;
		Refresh();

		if (SelectedId is { } id)
		{
			if (_store.Find(id) is null)
			{
				await ClearSelectionAsync().ConfigureAwait(false);
			}
			else
			{
				_run.Value = _runs.LatestRun(id);
				await PublishAsync(_draftBytes ?? Encoding.UTF8.GetBytes(_draft), CancellationToken.None)
					.ConfigureAwait(false);
			}
		}

		if (previousName is not null)
		{
			await RemoveQuietlyAsync(previousResources, previousName).ConfigureAwait(false);
		}
	}

	private async Task RunExclusiveAsync(Func<Task> work, UiState<AutoHotkeyPageStatus?>? failures = null)
	{
		await _work.WaitAsync().ConfigureAwait(false);
		try
		{
			if (_disposed)
			{
				return;
			}

			await work().ConfigureAwait(false);
		}
		catch (AutoHotkeyLibraryException ex)
		{
			Visible(failures).Value = new AutoHotkeyPageStatus("error", Describe(ex.Error));
		}
		catch (Exception ex) when (ex is IOException
			or UnauthorizedAccessException
			or UiResourceException
			or ArgumentException
			or ObjectDisposedException)
		{
			_logger.Warning(ex, "An AutoHotkey script page operation failed");
			Visible(failures).Value = new AutoHotkeyPageStatus("error", Strings.OperationFailed());
		}
		finally
		{
			_work.Release();
		}
	}

	private UiState<AutoHotkeyPageStatus?> Visible(UiState<AutoHotkeyPageStatus?>? target)
	{
		if (target is null
			|| ReferenceEquals(target, _importStatus) && !_importOpen.Value
			|| ReferenceEquals(target, _exportStatus) && _exportTarget.Value is null
			|| ReferenceEquals(target, _editStatus) && _editing.Value is null)
		{
			return _status;
		}

		return target;
	}

	private void OpenEditor(Guid? id)
	{
		var script = id is { } existing ? _store.Find(existing) : null;
		_editStatus.Value = null;
		_editName.Value = script?.Name ?? string.Empty;
		_editDescription.Value = script?.Description ?? string.Empty;
		_editing.Value = new AutoHotkeyEditTarget(script?.Id);
	}

	private void CloseEditor()
	{
		_editStatus.Value = null;
		_editing.Value = null;
	}

	private Task SubmitEditorAsync(CancellationToken cancellationToken)
	{
		return RunExclusiveAsync(async () =>
			{
				if (_editing.Value is not { } target)
				{
					return;
				}

				var name = _editName.Value.Trim();
				var description = _editDescription.Value.Trim();
				if (name.Length == 0)
				{
					Visible(_editStatus).Value = new AutoHotkeyPageStatus("error", Strings.InvalidName());
					return;
				}

				if (target.ScriptId is not { } id)
				{
					var created = await _store.CreateAsync(name, NewScriptSource, cancellationToken)
						.ConfigureAwait(false);
					if (description.Length > 0)
					{
						await _store.UpdateSettingsAsync(created.Id,
								SettingsOf(created) with { Description = description },
								cancellationToken)
							.ConfigureAwait(false);
					}

					CloseEditor();
					await SelectLockedAsync(created.Id, cancellationToken).ConfigureAwait(false);
					return;
				}

				var script = _store.Find(id) ?? throw new AutoHotkeyLibraryException(AutoHotkeyLibraryError.NotFound);
				if (!string.Equals(name, script.Name, StringComparison.Ordinal))
				{
					await _store.RenameAsync(id, name, cancellationToken).ConfigureAwait(false);
				}

				if (!string.Equals(description, script.Description ?? string.Empty, StringComparison.Ordinal))
				{
					await _store.UpdateSettingsAsync(id,
							SettingsOf(_store.Find(id)!) with { Description = description },
							cancellationToken)
						.ConfigureAwait(false);
					if (SelectedId == id)
					{
						_description.Value = description;
					}
				}

				CloseEditor();
				_dirty.Value = IsDirty();
			},
			_editStatus);
	}

	private void SetImportOpen(bool open)
	{
		_importStatus.Value = null;
		_importOpen.Value = open;
	}

	private void SetExportTarget(Guid? id)
	{
		_exportStatus.Value = null;
		_exportTarget.Value = id;
	}

	private string Name(LocalizedText text)
	{
		return AutoHotkeyText.Resolve(text, _culture);
	}

	private void SetSetting<T>(UiState<T> state, T value)
	{
		state.Value = value;
		_dirty.Value = IsDirty();
	}

	private bool IsDirty()
	{
		return SelectedId is { } id
			&& _store.Find(id) is { } script
			&& (!string.Equals(_draft, _savedSource, StringComparison.Ordinal)
				|| new AutoHotkeyScriptSettings(_description.Value,
					_startWithMacroDeck.Value,
					_includeHelper.Value,
					_workingDirectory.Value)
				!= SettingsOf(script));
	}

	private void LoadSettings(AutoHotkeyScript script)
	{
		_description.Value = script.Description ?? string.Empty;
		_startWithMacroDeck.Value = script.StartWithMacroDeck;
		_includeHelper.Value = script.IncludeHelper;
		_workingDirectory.Value = script.WorkingDirectory ?? string.Empty;
	}

	private static AutoHotkeyScriptSettings SettingsOf(AutoHotkeyScript script)
	{
		return new AutoHotkeyScriptSettings(script.Description ?? string.Empty,
			script.StartWithMacroDeck,
			script.IncludeHelper,
			script.WorkingDirectory ?? string.Empty);
	}

	private void OnLibraryChanged(AutoHotkeyLibraryChange change)
	{
		Refresh();
		if (change is { Kind: AutoHotkeyLibraryChangeKind.Deleted, ScriptId: { } deleted } && SelectedId == deleted)
		{
			_ = RunExclusiveAsync(ClearSelectionAsync);
		}
	}

	private void OnRunChanged(AutoHotkeyRunSnapshot snapshot)
	{
		if (snapshot.ScriptId is { } scriptId && scriptId == SelectedId)
		{
			_run.Value = snapshot;
		}

		_rows.Value = Rows();
	}

	private void Refresh()
	{
		_installed.Value = _host.Installation.IsValid;
		_libraryReadOnly.Value = _store.IsReadOnly;
		_rows.Value = Rows();
	}

	private IReadOnlyList<AutoHotkeyScriptRow> Rows()
	{
		return
		[
			.. _store.Scripts.OrderBy(script => script.Name, StringComparer.CurrentCultureIgnoreCase)
				.Select(script => new AutoHotkeyScriptRow(script.Id,
					script.Name,
					_runs.IsScriptRunning(script.Id)))
		];
	}

	private static LocalizedString RunState(AutoHotkeyRunSnapshot? run)
	{
		return run?.State switch
		{
			null => Strings.NotRunYet(),
			AutoHotkeyRunState.Starting => Strings.StatusStarting(),
			AutoHotkeyRunState.Running => StateStrings.Running(),
			AutoHotkeyRunState.FailedToStart => Strings.StatusFailedToStart(),
			AutoHotkeyRunState.TimedOut => Strings.StatusTimedOut(),
			_ => Strings.StatusExited(run.ExitCode ?? 0)
		};
	}

	private static LocalizedString ErrorText(AutoHotkeyScriptError? error)
	{
		return error?.Line is { } line
			? Strings.ErrorWithLine(line, error.Message)
			: Strings.ErrorNoLine(error?.Message ?? string.Empty);
	}

	private string Seconds(TimeSpan? duration)
	{
		return (duration ?? TimeSpan.Zero).TotalSeconds.ToString("0.0", _formatCulture);
	}

	private static CultureInfo FormatCulture(string? culture)
	{
		if (string.IsNullOrWhiteSpace(culture))
		{
			return CultureInfo.InvariantCulture;
		}

		try
		{
			return CultureInfo.GetCultureInfo(culture);
		}
		catch (CultureNotFoundException)
		{
			return CultureInfo.InvariantCulture;
		}
	}

	private static LocalizedText Describe(AutoHotkeyLibraryError error)
	{
		return error switch
		{
			AutoHotkeyLibraryError.ReadOnly => Strings.LibraryReadOnlyBanner(),
			AutoHotkeyLibraryError.NotFound => Strings.ScriptGone(),
			AutoHotkeyLibraryError.InvalidName => Strings.InvalidName(),
			AutoHotkeyLibraryError.SourceUnreadable => Strings.SourceReadOnlyBanner(),
			AutoHotkeyLibraryError.TooLarge => Strings.TooLarge(),
			AutoHotkeyLibraryError.NotUtf8 => Strings.ImportNotUtf8(),
			_ => Strings.OperationFailed()
		};
	}
}
