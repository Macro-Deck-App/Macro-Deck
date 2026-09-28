import { ConnectionEndpoint, GetConnectionInfoResponse } from '@macro-deck/runtime';
import { create } from 'qrcode';
import { encodeConnectLink } from './connect-link';

describe('connect link v3', () => {
  const http: ConnectionEndpoint = { address: '192.168.1.10', port: 8193, ssl: false };
  const https: ConnectionEndpoint = { address: '192.168.1.10', port: 8194, ssl: true };
  const info: GetConnectionInfoResponse = {
    instanceName: 'Test Instance',
    endpoints: [http],
    publicListenerUnavailable: false,
    version: '3.0.0-test',
  };

  const prefix = 'https://connect.macro-deck.app/';
  const referenceHost: GetConnectionInfoResponse = {
    ...info,
    instanceName: 'Companion test host',
    endpoints: [http, https],
  };

  function linkBytes(url: string): number[] {
    const digits = url.slice(prefix.length);
    const bytes: number[] = [];
    for (let i = 0; i < digits.length; i += 5) {
      const value = Number(digits.slice(i, i + 5));
      bytes.push(...(digits.length - i === 3 ? [value] : [value >> 8, value & 0xff]));
    }
    return bytes;
  }

  it('matches the conformance vector in engineering/api/connect-link.md', () => {
    expect(encodeConnectLink(referenceHost, '482915')).toBe(prefix +
      '00787172632801624942269912819229797295560829628531296980019243009025920025600192430090259200513015881438614641053');
  });

  it('matches the conformance vector with the identity fingerprint in engineering/api/connect-link.md', () => {
    const link = encodeConnectLink({ ...referenceHost, identityFingerprint: '3208 E004 6ED3 EE6B 4E75 1027' }, '482915');

    expect(link).toBe(prefix +
      '00787172632801624942269912819229797295560829628531296980019243009025920025600192430090259200513015881438614641136180227201134542542747029968039');
    expect(linkBytes(link).slice(-12)).toEqual([0x32, 0x08, 0xE0, 0x04, 0x6E, 0xD3, 0xEE, 0x6B, 0x4E, 0x75, 0x10, 0x27]);
  });

  it('ends at the token while the identity key is unavailable or the fingerprint is malformed', () => {
    const withoutKey = encodeConnectLink({ ...referenceHost, identityFingerprint: null }, '482915');
    const malformed = encodeConnectLink({ ...referenceHost, identityFingerprint: '3208 E004' }, '482915');

    expect(withoutKey).toBe(encodeConnectLink(referenceHost, '482915'));
    expect(malformed).toBe(withoutKey);
  });

  it('fits a far smaller QR code than the version 2 link did', () => {
    expect(create(encodeConnectLink(referenceHost, '482915'), { errorCorrectionLevel: 'L' }).version)
      .toBeLessThanOrEqual(5);
  });

  it('writes hostnames as text and skips addresses it cannot describe, with an empty token', () => {
    const link = encodeConnectLink({
      ...info,
      instanceName: 'H',
      endpoints: [
        { address: 'fe80::1', port: 8193, ssl: false },
        { address: 'deck.local', port: 8194, ssl: true },
        { address: '300.1.1.1', port: 8193, ssl: false },
        { address: 'a'.repeat(256), port: 8193, ssl: false },
      ],
    }, '');

    expect(link).toMatch(/^https:\/\/connect\.macro-deck\.app\/\d+$/);
    expect(linkBytes(link)).toEqual([
      3, 1, 0x48, 1,
      2, 10, ...Array.from('deck.local', (c) => c.charCodeAt(0)), 0x20, 0x02, 1,
      0,
    ]);
  });

  it('cuts an overlong instance name on a character boundary', () => {
    const bytes = linkBytes(encodeConnectLink({ ...info, instanceName: 'ü'.repeat(200), endpoints: [] }, ''));

    expect(bytes[1]).toBe(254);
    expect(new TextDecoder('utf-8', { fatal: true }).decode(new Uint8Array(bytes.slice(2, 2 + bytes[1]))))
      .toBe('ü'.repeat(127));
  });
});
