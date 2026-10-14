namespace MacroDeck.Sdk.Companion;

/// <summary>The answer to <see cref="ICompanionDeviceApi.CaptureScreenshotAsync" />: the PNG, or why there is none.</summary>
public sealed record CompanionScreenshotResult
{
	private CompanionScreenshotResult(byte[]? png, CompanionDeviceFailure? failure)
	{
		Png = png;
		Failure = failure;
	}

	public byte[]? Png { get; }

	/// <summary>Set exactly when <see cref="Png" /> is <c>null</c>.</summary>
	public CompanionDeviceFailure? Failure { get; }

	public static CompanionScreenshotResult Of(byte[] png)
	{
		ArgumentNullException.ThrowIfNull(png);
		return new CompanionScreenshotResult(png, null);
	}

	public static CompanionScreenshotResult Failed(CompanionDeviceFailure failure)
	{
		return new CompanionScreenshotResult(null, failure);
	}
}
