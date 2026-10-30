import { createHmac } from 'node:crypto';

// The same session code the bootstrapper mints for its window (ui/bootstrapper/src/loopback_secret.rs).
export function sessionCode(secret, nowSeconds, nonce) {
  const expiry = nowSeconds + 60;
  const tag = createHmac('sha256', Buffer.from(secret, 'hex'))
    .update(`macro-deck-loopback-code:${expiry}.${nonce}`)
    .digest('hex');
  return `${expiry}.${nonce}.${tag}`;
}
