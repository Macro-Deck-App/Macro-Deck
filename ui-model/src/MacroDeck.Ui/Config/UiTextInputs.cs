using MacroDeck.Ui.Dsl;

namespace MacroDeck.Ui.Config;

/// <summary>
/// A single-line or multi-line text field. Counterpart of the existing <c>String</c> parameter type, which the
/// editor renders as its parameter input.
///
/// <para>
/// <b>Multi-line text is this primitive, not a second one.</b> The existing schema has no multi-line type
/// either: its multi-line factory produces a <c>String</c> parameter with the multi-line flag set, and the
/// editor's text control switches on the flag. Adding a <c>multiline-text</c> type here would give this
/// vocabulary a member the enum it was derived from does not have, so a renderer would have to know a mapping
/// that exists nowhere else.
/// </para>
/// </summary>
public sealed record UiStringInput : UiInput<string>
{
	/// <summary>Whether the field accepts line breaks.</summary>
	public UiValue<bool> Multiline { get; init; }

	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.String;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiConfigProperties.Multiline, Multiline);
	}
}

/// <summary>A masked field whose value is stored as a secret. Counterpart of the <c>Password</c> parameter
/// type, which the editor renders as its secret input.</summary>
public sealed record UiPasswordInput : UiInput<string>
{
	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.Password;
}

/// <summary>A masked field whose value is stored encrypted. Counterpart of the <c>Secret</c> parameter type,
/// which shares the editor's secret input with a password and differs in what it means, not how it looks.
/// </summary>
public sealed record UiSecretInput : UiInput<string>
{
	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.Secret;
}

/// <summary>
/// A URL field. Counterpart of the <c>Url</c> parameter type, whose validation rejects a value the browser
/// would silently repair - <c>https:/example.com</c> - rather than accepting a link that goes nowhere.
/// </summary>
public sealed record UiUrlInput : UiInput<string>
{
	/// <summary>Whether the client adds <c>https://</c> to a value entered without a protocol. Counterpart of
	/// the parameter schema's own flag, which the editor applies when the field loses focus.</summary>
	public UiValue<bool> AutoPrefixHttps { get; init; }

	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.Url;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiConfigProperties.AutoPrefixHttps, AutoPrefixHttps);
	}
}

/// <summary>An IPv4 or IPv6 address. Counterpart of the <c>IpAddress</c> parameter type, which the editor
/// renders as a text field placeholdered with an address and validates as one.</summary>
public sealed record UiIpAddressInput : UiInput<string>
{
	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.IpAddress;
}

/// <summary>
/// A JSON document. Counterpart of the <c>Json</c> parameter type, which the editor renders in its code editor
/// with JSON highlighting. The value is the document's <b>text</b>, as the editor edits it and as validation
/// parses it, so a value that does not parse is still round-tripped rather than lost on the way to the error
/// message.
/// </summary>
public sealed record UiJsonInput : UiInput<string>
{
	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.Json;
}

/// <summary>Source code in a named language. Counterpart of the <c>Code</c> parameter type, which the editor
/// renders in its code editor with the parameter's language.</summary>
public sealed record UiCodeInput : UiInput<string>
{
	/// <summary>The language the editor highlights. Counterpart of the parameter schema's <c>Language</c>.
	/// </summary>
	public UiValue<string> Language { get; init; }

	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.Code;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiConfigProperties.Language, Language);
	}
}
