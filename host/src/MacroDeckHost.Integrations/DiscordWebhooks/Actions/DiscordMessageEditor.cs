using System.Globalization;
using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Integrations.DiscordWebhooks.Models;
using MacroDeckHost.Integrations.DiscordWebhooks.Protocol;
using Serilog;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.DiscordWebhooks;

namespace MacroDeckHost.Integrations.DiscordWebhooks.Actions;

internal sealed record EditorStatus(string Severity, LocalizedText Text);

// Every edit reports the rebuilt message map while its event is still being dispatched, so a save started
// right after the edit includes it.
internal sealed class DiscordMessageEditor
{
	public const string PingNone = "none";
	public const string PingAll = "all";
	public const string PingListed = "listed";

	private readonly IReadOnlyList<ConfigEntrySnapshot> _entries;
	private readonly DiscordWebhookTargets _targets;
	private readonly IDiscordWebhookSender _sender;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;
	private readonly string _clock;

	private readonly UiState<string> _webhook;
	private readonly UiState<string> _webhookUrl;
	private readonly UiState<string> _content;
	private readonly UiState<string> _username;
	private readonly UiState<string> _avatarUrl;
	private readonly UiState<string> _userMode;
	private readonly UiState<string> _userIds;
	private readonly UiState<string> _roleMode;
	private readonly UiState<string> _roleIds;
	private readonly UiState<bool> _pingEveryone;
	private readonly UiState<IReadOnlyDictionary<string, string>> _names;
	private readonly UiState<IReadOnlyList<EmbedEditorState>> _embeds;
	private readonly UiState<bool> _armed = new(false);
	private readonly UiState<EditorStatus?> _status = new(null);

	private string _lastReported;

	public DiscordMessageEditor(
		IReadOnlyDictionary<string, JsonElement> stored,
		IReadOnlyList<ConfigEntrySnapshot> entries,
		DiscordWebhookTargets targets,
		IDiscordWebhookSender sender,
		TimeProvider time,
		ILogger logger)
	{
		_entries = entries;
		_targets = targets;
		_sender = sender;
		_time = time;
		_logger = logger;
		_clock = time.GetLocalNow().ToString("t", CultureInfo.CurrentCulture);

		var draft = DiscordMessageMap.Parse(stored.TryGetValue(SendMessageActionDefinition.MessageParameter,
			out var message)
			? DiscordMessageMap.Read(message)
			: new Dictionary<string, string>(StringComparer.Ordinal));

		_webhook = new UiState<string>(ReadString(stored, SendMessageActionDefinition.WebhookParameter));
		_webhookUrl = new UiState<string>(ReadString(stored, SendMessageActionDefinition.WebhookUrlParameter));
		_content = new UiState<string>(draft.Content);
		_username = new UiState<string>(draft.Username);
		_avatarUrl = new UiState<string>(draft.AvatarUrl);
		_userMode = new UiState<string>(PingMode(draft.PingUsers, draft.AllowedUsers));
		_userIds = new UiState<string>(string.Join(", ", draft.AllowedUsers));
		_roleMode = new UiState<string>(PingMode(draft.PingRoles, draft.AllowedRoles));
		_roleIds = new UiState<string>(string.Join(", ", draft.AllowedRoles));
		_pingEveryone = new UiState<bool>(draft.PingEveryone);
		_names = new UiState<IReadOnlyDictionary<string, string>>(draft.Names);
		_embeds = new UiState<IReadOnlyList<EmbedEditorState>>(draft.Embeds
			.Select(embed => new EmbedEditorState(embed))
			.ToList());

		_lastReported = Serialize(DiscordMessageMap.ToMap(Draft())).GetRawText();
	}

	public Action<string, JsonElement>? Report { get; set; }

	public DiscordMessageDraft Draft()
	{
		return new DiscordMessageDraft
		{
			Content = _content.Value,
			Username = _username.Value,
			AvatarUrl = _avatarUrl.Value.Trim(),
			PingUsers = _userMode.Value == PingAll,
			AllowedUsers = _userMode.Value == PingListed ? DiscordMessageMap.SplitList(_userIds.Value) : [],
			PingRoles = _roleMode.Value == PingAll,
			AllowedRoles = _roleMode.Value == PingListed ? DiscordMessageMap.SplitList(_roleIds.Value) : [],
			PingEveryone = _pingEveryone.Value,
			Embeds = _embeds.Value.Select(embed => embed.ToDraft()).ToList(),
			Names = _names.Value
				.Where(pair => pair.Key.Trim().Length > 0
					&& pair.Key.Trim().All(char.IsAsciiDigit)
					&& pair.Value.Trim().Length > 0)
				.ToDictionary(pair => pair.Key.Trim(), pair => pair.Value.Trim(), StringComparer.Ordinal)
		};
	}

	public UiElement Build()
	{
		return new UiConfigStack
		{
			Key = "discord-message",
			Children =
			[
				WebhookPicker(),
				new UiWhen
				{
					Key = "webhook-url-when",
					Condition = ShowsInlineUrl,
					Content = () => new UiSecretInput
					{
						Key = SendMessageActionDefinition.WebhookUrlParameter,
						Label = Strings.Actions.WebhookUrlLabel(),
						Description = Strings.Actions.WebhookUrlDescription(),
						Binding = EditText(_webhookUrl)
					}
				},
				new UiTabs
				{
					Key = "tabs",
					Children = [MessageTab(), EmbedsTab(), ProfileTab(), MentionsTab()]
				},
				Validation(),
				new UiHeading { Key = "preview-heading", Text = Strings.Editor.PreviewHeading() },
				new UiConfigStack
				{
					Key = "preview",
					Accent = DiscordPreviewPalette.Surface,
					Children =
					[
						new UiRepeat<UiElement>
						{
							Key = "preview-items",
							Items = UiValue.From(() => DiscordPreviewBuilder.Build(Draft(), _clock)),
							KeySelector = element => element.Key,
							Template = (element, _) => element
						}
					]
				},
				new UiProse
				{
					Key = "preview-note",
					Text = Strings.Editor.PreviewNote(),
					Variant = DiscordMarkdown.SubtextVariant
				},
				TestSend()
			]
		};
	}

	internal async Task SendTestAsync(CancellationToken cancellationToken)
	{
		if (!_armed.Value)
		{
			return;
		}

		_armed.Value = false;
		var draft = Draft();
		if (DiscordMessageValidator.Validate(draft, true).Count > 0)
		{
			_status.Value = new EditorStatus("error", Strings.Editor.TestInvalid());
			return;
		}

		string? inlineUrl = null;
		if (!IsSavedWebhook(_webhook.Value))
		{
			inlineUrl = _webhookUrl.Value.Trim();
			if (inlineUrl == UiConfigSurfaceAttributes.MaskedSecretValue)
			{
				_status.Value = new EditorStatus("warning", Strings.Editor.TestReenterUrl());
				return;
			}
		}

		_status.Value = new EditorStatus("info", Strings.Editor.TestSending());

		var target = await _targets.ResolveAsync(_webhook.Value, inlineUrl, cancellationToken).ConfigureAwait(false);
		if (target is not { Kind: DiscordWebhookTargetKind.Resolved, Url: { } url })
		{
			_status.Value = new EditorStatus("error",
				target.Kind == DiscordWebhookTargetKind.NoWebhook
					? Strings.Editor.TestNoWebhook()
					: DiscordWebhookErrors.Describe(target.Kind).Message);
			return;
		}

		var payload = DiscordPayloadBuilder.Build(draft, _time.GetUtcNow());
		var result = await _sender.SendAsync(url, payload, cancellationToken).ConfigureAwait(false);
		_logger.Information("Discord webhook test message to {Webhook} answered {Outcome} ({Status})",
			target.LogName,
			result.Outcome,
			result.Status);

		_status.Value = result.Outcome == DiscordSendOutcome.Sent
			? new EditorStatus("success", Strings.Editor.TestSent())
			: new EditorStatus("error", DiscordWebhookErrors.Describe(result).Message);
	}

	private static string PingMode(bool all, IReadOnlyList<string> ids)
	{
		return ids.Count > 0 ? PingListed : all ? PingAll : PingNone;
	}

	private static string ReadString(IReadOnlyDictionary<string, JsonElement> stored, string name)
	{
		return stored.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString() ?? string.Empty
			: string.Empty;
	}

	private static JsonElement Serialize(Dictionary<string, string> map)
	{
		return JsonSerializer.SerializeToElement(new SortedDictionary<string, string>(map, StringComparer.Ordinal));
	}

	private static string LastSegment(UiEventData data)
	{
		if (!data.TryGetString(out var id))
		{
			return string.Empty;
		}

		var dot = id.LastIndexOf('.');
		return dot >= 0 ? id[(dot + 1)..] : id;
	}

	private bool IsSavedWebhook(string value)
	{
		return Guid.TryParse(value, out var id) && _entries.Any(entry => entry.Id == id);
	}

	private bool ShowsInlineUrl()
	{
		var value = _webhook.Value;
		return value == DiscordWebhookTargets.Custom || value.Length == 0 && _entries.Count == 0;
	}

	private void Edited()
	{
		_armed.Value = false;
		_status.Value = null;

		var map = Serialize(DiscordMessageMap.ToMap(Draft()));
		var text = map.GetRawText();
		if (text == _lastReported)
		{
			return;
		}

		_lastReported = text;
		Report?.Invoke(SendMessageActionDefinition.MessageParameter, map);
	}

	private UiBinding<string> EditText(UiState<string> state)
	{
		return Bind.Custom(() => state.Value,
			value =>
			{
				state.Value = value ?? string.Empty;
				Edited();
			});
	}

	private UiBinding<T> Edit<T>(UiState<T> state)
	{
		return Bind.Custom(() => state.Value,
			value =>
			{
				state.Value = value;
				Edited();
			});
	}

	private UiChoiceInput WebhookPicker()
	{
		IReadOnlyList<UiOption> options =
		[
			.. _entries.Select(entry => UiOption.Of(entry.Id.ToString(), entry.Title)),
			UiOption.Of(DiscordWebhookTargets.Custom, Strings.Actions.CustomWebhook())
		];

		return new UiChoiceInput
		{
			Key = SendMessageActionDefinition.WebhookParameter,
			Label = Strings.Actions.WebhookLabel(),
			Description = _entries.Count == 0
				? Strings.Actions.NoWebhooksSaved()
				: Strings.Actions.WebhookDescription(),
			Placeholder = Strings.Actions.WebhookPlaceholder(),
			Options = UiValue.Of(options),
			Binding = EditText(_webhook)
		};
	}

	private UiTab MessageTab()
	{
		return new UiTab
		{
			Key = "message-tab",
			Label = Strings.Editor.TabMessage(),
			Children =
			[
				new UiStringInput
				{
					Key = "content",
					Label = Strings.Editor.ContentLabel(),
					Description = Strings.Editor.ContentDescription(),
					Multiline = true,
					Binding = EditText(_content)
				},
				new UiConfigStack
				{
					Key = "mention-tools",
					Direction = "horizontal",
					Children =
					[
						MentionPrompt("mention-user", Strings.Editor.InsertUserMention(), "<@{0}>"),
						MentionPrompt("mention-role", Strings.Editor.InsertRoleMention(), "<@&{0}>"),
						new UiConfigButton
						{
							Key = "mention-everyone",
							Label = Strings.Editor.InsertEveryone(),
							Events = [UiEventHandler.On(UiConfigEvents.Activate, () => Append("@everyone"))]
						},
						new UiConfigButton
						{
							Key = "mention-here",
							Label = Strings.Editor.InsertHere(),
							Events = [UiEventHandler.On(UiConfigEvents.Activate, () => Append("@here"))]
						}
					]
				}
			]
		};
	}

	private UiConfigButton MentionPrompt(string key, LocalizedText label, string format)
	{
		return new UiConfigButton
		{
			Key = key,
			Label = label,
			ConfirmLabel = Strings.Editor.Insert(),
			PromptValue = string.Empty,
			Placeholder = Strings.Editor.MentionIdPlaceholder(),
			Events =
			[
				UiEventHandler.On(UiConfigEvents.Activate,
					data =>
					{
						if (data.TryGetString(out var id) && id.Trim().Length > 0 && id.Trim().All(char.IsAsciiDigit))
						{
							Append(string.Format(CultureInfo.InvariantCulture, format, id.Trim()));
						}
					})
			]
		};
	}

	private void Append(string mention)
	{
		var current = _content.Value;
		_content.Value = current.Length == 0 || char.IsWhiteSpace(current[^1])
			? current + mention
			: current + " " + mention;
		Edited();
	}

	private UiTab EmbedsTab()
	{
		return new UiTab
		{
			Key = "embeds-tab",
			Label = Strings.Editor.TabEmbeds(),
			Children =
			[
				new UiArrayInput
				{
					Key = "embeds",
					Events =
					[
						UiEventHandler.On(UiConfigEvents.Add, AddEmbed),
						UiEventHandler.On(UiConfigEvents.Remove, data => RemoveEmbed(LastSegment(data)))
					],
					Children =
					[
						new UiRepeat<EmbedEditorState>
						{
							Key = "embed-items",
							Items = UiValue.From(() => _embeds.Value),
							KeySelector = embed => embed.Id,
							Template = (embed, _) => EmbedItem(embed)
						}
					]
				}
			]
		};
	}

	private void AddEmbed()
	{
		if (_embeds.Value.Count >= DiscordMessageValidator.EmbedLimit)
		{
			return;
		}

		_embeds.Value = [.. _embeds.Value, new EmbedEditorState(new DiscordEmbedDraft())];
		Edited();
	}

	private void RemoveEmbed(string id)
	{
		_embeds.Value = _embeds.Value.Where(embed => embed.Id != id).ToList();
		Edited();
	}

	private UiObjectInput EmbedItem(EmbedEditorState embed)
	{
		var id = embed.Id;

		return new UiObjectInput
		{
			Key = id,
			Children =
			[
				new UiHeading
				{
					Key = $"embed-heading-{id}",
					Text = UiText.FromLocalized(()
						=> Strings.Editor.EmbedHeading(_embeds.Value.ToList().IndexOf(embed) + 1))
				},
				Text("title", Strings.Editor.TitleLabel(), embed.Title),
				Text("description", Strings.Editor.DescriptionLabel(), embed.Description, true),
				Url("url", Strings.Editor.UrlLabel(), embed.Url),
				new UiColorInput
				{
					Key = "color",
					Label = Strings.Editor.ColorLabel(),
					SupportsReset = true,
					DefaultValue = string.Empty,
					Binding = EditText(embed.Color)
				},
				new UiChoiceInput
				{
					Key = "timestampMode",
					Label = Strings.Editor.TimestampLabel(),
					Segmented = true,
					Options = UiValue.Of<IReadOnlyList<UiOption>>([
						UiOption.Of(EmbedEditorState.TimestampNone, Strings.Editor.TimestampNone()),
						UiOption.Of(EmbedEditorState.TimestampFixed, Strings.Editor.TimestampFixed()),
						UiOption.Of(EmbedEditorState.TimestampNow, Strings.Editor.TimestampNow())
					]),
					Binding = EditText(embed.TimestampMode)
				},
				new UiWhen
				{
					Key = $"timestamp-when-{id}",
					Condition = () => embed.TimestampMode.Value == EmbedEditorState.TimestampFixed,
					Content = () => new UiDateTimeInput
					{
						Key = "timestamp",
						Label = Strings.Editor.TimestampValueLabel(),
						Binding = EditText(embed.TimestampValue)
					}
				},
				Text("authorName", Strings.Editor.AuthorNameLabel(), embed.AuthorName),
				Url("authorUrl", Strings.Editor.AuthorUrlLabel(), embed.AuthorUrl),
				Url("authorIcon", Strings.Editor.AuthorIconLabel(), embed.AuthorIconUrl),
				Text("footerText", Strings.Editor.FooterTextLabel(), embed.FooterText),
				Url("footerIcon", Strings.Editor.FooterIconLabel(), embed.FooterIconUrl),
				Url("image", Strings.Editor.ImageLabel(), embed.Image),
				Url("thumbnail", Strings.Editor.ThumbnailLabel(), embed.Thumbnail),
				new UiHeading { Key = $"fields-heading-{id}", Text = Strings.Editor.FieldsHeading() },
				new UiArrayInput
				{
					Key = "fields",
					Events =
					[
						UiEventHandler.On(UiConfigEvents.Add, () => AddField(embed)),
						UiEventHandler.On(UiConfigEvents.Remove, data => RemoveField(embed, LastSegment(data)))
					],
					Children =
					[
						new UiRepeat<FieldEditorState>
						{
							Key = $"field-items-{id}",
							Items = UiValue.From(() => embed.Fields.Value),
							KeySelector = field => field.Id,
							Template = (field, _) => FieldItem(field)
						}
					]
				}
			]
		};
	}

	private void AddField(EmbedEditorState embed)
	{
		if (embed.Fields.Value.Count >= DiscordMessageValidator.FieldLimit)
		{
			return;
		}

		embed.Fields.Value =
		[
			.. embed.Fields.Value, new FieldEditorState(new DiscordFieldDraft(string.Empty,
				string.Empty,
				false))
		];
		Edited();
	}

	private void RemoveField(EmbedEditorState embed, string id)
	{
		embed.Fields.Value = embed.Fields.Value.Where(field => field.Id != id).ToList();
		Edited();
	}

	private UiObjectInput FieldItem(FieldEditorState field)
	{
		return new UiObjectInput
		{
			Key = field.Id,
			Children =
			[
				Text("name", Strings.Editor.FieldNameLabel(), field.Name),
				Text("value", Strings.Editor.FieldValueLabel(), field.Value, true),
				new UiBooleanInput
				{
					Key = "inline",
					Label = Strings.Editor.FieldInlineLabel(),
					Binding = Edit(field.Inline)
				}
			]
		};
	}

	private UiTab ProfileTab()
	{
		return new UiTab
		{
			Key = "profile-tab",
			Label = Strings.Editor.TabProfile(),
			Children =
			[
				new UiStringInput
				{
					Key = "username",
					Label = Strings.Editor.UsernameLabel(),
					Description = Strings.Editor.UsernameDescription(),
					Binding = EditText(_username)
				},
				new UiUrlInput
				{
					Key = "avatarUrl",
					Label = Strings.Editor.AvatarLabel(),
					Description = Strings.Editor.AvatarDescription(),
					Binding = EditText(_avatarUrl)
				}
			]
		};
	}

	private UiTab MentionsTab()
	{
		return new UiTab
		{
			Key = "mentions-tab",
			Label = Strings.Editor.TabMentions(),
			Children =
			[
				new UiProse { Key = "mentions-description", Text = Strings.Editor.MentionsDescription() },
				PingChoice("pingUsers", Strings.Editor.UsersLabel(), _userMode),
				new UiWhen
				{
					Key = "user-ids-when",
					Condition = () => _userMode.Value == PingListed,
					Content = () => IdList("userIds", Strings.Editor.UserIdsLabel(), _userIds)
				},
				PingChoice("pingRoles", Strings.Editor.RolesLabel(), _roleMode),
				new UiWhen
				{
					Key = "role-ids-when",
					Condition = () => _roleMode.Value == PingListed,
					Content = () => IdList("roleIds", Strings.Editor.RoleIdsLabel(), _roleIds)
				},
				new UiBooleanInput
				{
					Key = "pingEveryone",
					Label = Strings.Editor.EveryoneLabel(),
					Binding = Edit(_pingEveryone)
				},
				new UiKeyValueInput
				{
					Key = "previewNames",
					Label = Strings.Editor.NamesLabel(),
					Description = Strings.Editor.NamesDescription(),
					Binding = Edit(_names)
				}
			]
		};
	}

	private UiChoiceInput PingChoice(string key, LocalizedText label, UiState<string> state)
	{
		return new UiChoiceInput
		{
			Key = key,
			Label = label,
			Options = UiValue.Of<IReadOnlyList<UiOption>>([
				UiOption.Of(PingNone, Strings.Editor.PingNone()),
				UiOption.Of(PingAll, Strings.Editor.PingAll()),
				UiOption.Of(PingListed, Strings.Editor.PingListed())
			]),
			Binding = EditText(state)
		};
	}

	private UiStringInput IdList(string key, LocalizedText label, UiState<string> state)
	{
		return new UiStringInput
		{
			Key = key,
			Label = label,
			Description = Strings.Editor.IdsDescription(),
			LiteralOnly = true,
			Binding = EditText(state)
		};
	}

	private UiStringInput Text(string key, LocalizedText label, UiState<string> state, bool multiline = false)
	{
		return new UiStringInput { Key = key, Label = label, Multiline = multiline, Binding = EditText(state) };
	}

	private UiUrlInput Url(string key, LocalizedText label, UiState<string> state)
	{
		return new UiUrlInput { Key = key, Label = label, Binding = EditText(state) };
	}

	private UiConfigStack Validation()
	{
		return new UiConfigStack
		{
			Key = "validation",
			Children =
			[
				new UiRepeat<DiscordViolation>
				{
					Key = "violations",
					Items = UiValue.From(() => DiscordMessageValidator.Validate(Draft(), true)),
					KeySelector = violation => violation.Key,
					Template = (violation, _) => new UiBanner
					{
						Key = "violation-" + violation.Key,
						Severity = "error",
						Text = violation.Message
					}
				}
			]
		};
	}

	private UiConfigStack TestSend()
	{
		return new UiConfigStack
		{
			Key = "test-send",
			Children =
			[
				new UiWhen
				{
					Key = "test-idle-when",
					Condition = () => !_armed.Value,
					Content = () => new UiConfigButton
					{
						Key = "test-send-start",
						Label = Strings.Editor.TestSend(),
						Events =
						[
							UiEventHandler.On(UiConfigEvents.Activate,
								() =>
								{
									_status.Value = null;
									_armed.Value = true;
								})
						]
					}
				},
				new UiWhen
				{
					Key = "test-armed-when",
					Condition = () => _armed.Value,
					Content = () => new UiFragment
					{
						Key = "test-armed",
						Children =
						[
							new UiBanner
							{
								Key = "test-armed-banner",
								Severity = "warning",
								Text = Strings.Editor.TestSendArmed()
							},
							new UiConfigStack
							{
								Key = "test-armed-buttons",
								Direction = "horizontal",
								Children =
								[
									new UiConfigButton
									{
										Key = "test-send-now",
										Label = Strings.Editor.TestSendNow(),
										Events = [UiEventHandler.OnAsync(UiConfigEvents.Activate, SendTestAsync)]
									},
									new UiConfigButton
									{
										Key = "test-send-cancel",
										Label = MacroDeckStrings.Common.Cancel(),
										Events =
										[
											UiEventHandler.On(UiConfigEvents.Activate, () => _armed.Value = false)
										]
									}
								]
							}
						]
					}
				},
				new UiWhen
				{
					Key = "test-status-when",
					Condition = () => _status.Value is not null,
					Content = () => new UiBanner
					{
						Key = "test-status",
						Severity = UiValue.From(() => _status.Value?.Severity ?? "info"),
						Text = UiText.Optional(() => _status.Value is { } status ? status.Text : UiText.None())
					}
				}
			]
		};
	}
}
