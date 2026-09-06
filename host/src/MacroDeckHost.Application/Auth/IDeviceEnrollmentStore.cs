namespace MacroDeckHost.Application.Auth;

/// <summary>
/// Holds the outstanding one-time device-enrollment credentials.
///
/// A store of its own, and a singleton, because <c>IAuthService</c> is scoped: a credential minted
/// while handling one request has to still be there when the device turns up with it on the next.
/// Deliberately in memory - these live minutes and are spent once, so a host restart invalidating one
/// costs a re-run of device setup rather than warranting a table.
/// </summary>
public interface IDeviceEnrollmentStore
{
	/// <summary>Records a minted credential by hash.</summary>
	void Add(string tokenHash, DateTime expiresAt);

	/// <summary>
	/// Removes the credential and reports when it expired. Removal happens whether or not it was
	/// still valid, so a wrong guess and a right one both spend the attempt.
	/// </summary>
	bool TryConsume(string tokenHash, out DateTime expiresAt);

	/// <summary>Drops everything that has expired.</summary>
	void PurgeExpired(DateTime now);
}
