import { GetConnectionInfoResponse } from '@macro-deck/runtime';

const IPV4 = /^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$/;
const HOSTNAME = /^[A-Za-z0-9.-]+$/;
const FINGERPRINT = /^[0-9A-Fa-f]{24}$/;

export function encodeConnectLink(info: GetConnectionInfoResponse, token: string): string {
  const encoder = new TextEncoder();
  const name = new Uint8Array(255);
  const nameLength = encoder.encodeInto(info.instanceName, name).written;
  const endpoints = info.endpoints
    .map((endpoint) => {
      const address = encodeAddress(endpoint.address);
      return address && [...address, endpoint.port >> 8, endpoint.port & 0xff, endpoint.ssl ? 1 : 0];
    })
    .filter((endpoint): endpoint is number[] => !!endpoint)
    .slice(0, 255);
  const tokenBytes = encoder.encode(token);
  const bytes = [3, nameLength, ...name.subarray(0, nameLength), endpoints.length, ...endpoints.flat(),
    tokenBytes.length, ...tokenBytes, ...encodeFingerprint(info.identityFingerprint)];

  let digits = '';
  for (let i = 0; i < bytes.length; i += 2) {
    digits += i + 1 < bytes.length
      ? String(bytes[i] * 256 + bytes[i + 1]).padStart(5, '0')
      : String(bytes[i]).padStart(3, '0');
  }
  return `https://connect.macro-deck.app/${digits}`;
}

// Readers reject anything after the token but exactly 0 or 12 bytes, so a malformed value is left out.
function encodeFingerprint(fingerprint: string | null | undefined): number[] {
  const hex = fingerprint?.replaceAll(' ', '') ?? '';
  return FINGERPRINT.test(hex) ? Array.from({ length: 12 }, (_, i) => parseInt(hex.slice(i * 2, i * 2 + 2), 16)) : [];
}

function encodeAddress(address: string): number[] | null {
  const octets = IPV4.exec(address)?.slice(1).map(Number);
  if (octets) {
    return octets.every((octet) => octet <= 255) ? [0, ...octets] : null;
  }
  return HOSTNAME.test(address) && address.length <= 255
    ? [2, address.length, ...new TextEncoder().encode(address)]
    : null;
}
