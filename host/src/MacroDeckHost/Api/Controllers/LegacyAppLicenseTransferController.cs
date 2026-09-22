using System.Text.Json;
using MacroDeckHost.Application.Licensing;
using MacroDeckHost.Application.Ui.Transport.Messages.Licensing;
using MacroDeckHost.Infrastructure.Licensing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace MacroDeckHost.Api.Controllers;

public record LegacyLicenseTransferRequest(string? Platform, string? LegacyKind, string? SignedPayload);

public record LegacyLicenseTransferResponse(string Status, string? Code);

// Called by the Macro Deck 2 iOS app, which cannot speak the UI protocol; see engineering/api/macro-deck-2-app.md.
[ApiController]
[Route("api/legacy/md2-app")]
public class LegacyAppLicenseTransferController : ControllerBase
{
	public const string Md2AppBundleId = "com.suchbyte.macrodeck";
	public const int MaxSignedPayloadLength = 16 * 1024;

	private readonly ICompanionLicenseService _licenses;

	public LegacyAppLicenseTransferController(ICompanionLicenseService licenses)
	{
		_licenses = licenses;
	}

	[HttpPost("license-transfer")]
	[AllowAnonymous]
	[RequestSizeLimit(64 * 1024)]
	public async Task<IActionResult> Transfer(LegacyLicenseTransferRequest body)
	{
		if (body.Platform != CompanionLicenseSources.AppStoreLegacy ||
			body.LegacyKind != CompanionLicenseSources.AppTransactionKind)
		{
			return Answer(new LegacyPurchaseTransferResult(LegacyPurchaseTransferStatus.Rejected, "unsupported-source"));
		}

		if (body.SignedPayload is not { Length: > 0 and <= MaxSignedPayloadLength } signedPayload ||
			BundleId(signedPayload) is not { } bundleId)
		{
			return Answer(new LegacyPurchaseTransferResult(LegacyPurchaseTransferStatus.Rejected, "invalid-proof"));
		}

		// Unverified, only to keep proofs of other apps away from the shared queue and the Platform.
		if (bundleId != Md2AppBundleId)
		{
			return Answer(new LegacyPurchaseTransferResult(LegacyPurchaseTransferStatus.Rejected, "bundle-mismatch"));
		}

		var result = await _licenses.TransferLegacyPurchaseAsync(new CompanionLicenseProof
			{
				Platform = CompanionLicenseSources.AppStoreLegacy,
				LegacyKind = CompanionLicenseSources.AppTransactionKind,
				SignedPayload = signedPayload,
				ProductId = CompanionLicenseTokens.Product
			},
			HttpContext.RequestAborted);
		return Answer(result);
	}

	private IActionResult Answer(LegacyPurchaseTransferResult result)
	{
		var status = result.Status switch
		{
			LegacyPurchaseTransferStatus.Transferred => "transferred",
			LegacyPurchaseTransferStatus.AlreadyTransferred => "alreadyTransferred",
			LegacyPurchaseTransferStatus.Pending => "pending",
			LegacyPurchaseTransferStatus.Rejected => "rejected",
			_ => null
		};
		if (status is null)
		{
			return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
				title: "The purchase could not be queued. Try again later.");
		}

		return Ok(new LegacyLicenseTransferResponse(status, result.Code));
	}

	private static string? BundleId(string signedPayload)
	{
		var parts = signedPayload.Split('.');
		if (parts.Length != 3)
		{
			return null;
		}

		try
		{
			using var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(parts[1]));
			return payload.RootElement.ValueKind == JsonValueKind.Object &&
				payload.RootElement.TryGetProperty("bundleId", out var bundleId) &&
				bundleId.ValueKind == JsonValueKind.String
					? bundleId.GetString()
					: null;
		}
		catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
		{
			return null;
		}
	}
}
