// Safari only added isSecureContext in 12.1; reading the missing property as false mislabelled
// every capability gated on it (wake lock, install, trust probe) on the old devices this exists for.
export function isSecureContext(win: Pick<Window, 'isSecureContext' | 'location'> = window): boolean {
  if (typeof win.isSecureContext === 'boolean') {
    return win.isSecureContext;
  }

  const { protocol, hostname } = win.location;

  return protocol === 'https:' || hostname === 'localhost' || hostname === '127.0.0.1' || hostname === '[::1]';
}
