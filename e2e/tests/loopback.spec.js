import { expect, test } from '@playwright/test';
import { sessionCode } from './loopback.js';

test('the E2E session code matches the shared vector', () => {
  expect(sessionCode(
    '0f1e2d3c4b5a69788796a5b4c3d2e1f000112233445566778899aabbccddeeff',
    1_900_000_000,
    '00112233445566778899aabbccddeeff',
  )).toBe('1900000060.00112233445566778899aabbccddeeff.c8ba672b12783beb854f70711c5a5a6df1e45a53ef4abec3163c636c932feb73');
});
