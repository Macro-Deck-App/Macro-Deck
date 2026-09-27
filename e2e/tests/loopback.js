import { createHmac, randomBytes } from 'node:crypto';

export function loopbackSecret() {
  const secret = process.env.MACRODECK_LOOPBACK_SECRET;
  if (!secret) {
    throw new Error('MACRODECK_LOOPBACK_SECRET must be exported for the host and for Playwright');
  }
  return secret;
}

export function loopbackHeaders() {
  return { 'X-MacroDeck-Loopback-Secret': loopbackSecret() };
}

// The same session code the bootstrapper mints for its window (ui/bootstrapper/src/loopback_secret.rs).
export function sessionCode(secret, nowSeconds, nonce) {
  const expiry = nowSeconds + 60;
  const tag = createHmac('sha256', Buffer.from(secret, 'hex'))
    .update(`macro-deck-loopback-code:${expiry}.${nonce}`)
    .digest('hex');
  return `${expiry}.${nonce}.${tag}`;
}

export async function openDesktop(page) {
  const code = sessionCode(loopbackSecret(), Math.floor(Date.now() / 1000), randomBytes(16).toString('hex'));
  await page.goto(`/api/auth/loopback-session?code=${code}`);
}
