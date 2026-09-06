import { UserAgentDataLike, buildProposedName, parseBrowser, parseFormFactor, parsePlatform } from './user-agent.util';

interface UaCase {
  label: string;
  userAgent: string;
  userAgentData?: UserAgentDataLike;
  maxTouchPoints?: number;
  browser: string | null;
  platform: string | null;
  formFactor: 'phone' | 'tablet' | 'desktop';
  proposedName: string;
}

const CASES: UaCase[] = [
  {
    label: 'Chrome on Windows',
    userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) '
      + 'Chrome/141.0.0.0 Safari/537.36',
    browser: 'Chrome 141',
    platform: 'Windows',
    formFactor: 'desktop',
    proposedName: 'Chrome 141 on Windows – Desktop',
  },
  {
    label: 'Chrome on macOS',
    userAgent: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) '
      + 'Chrome/141.0.0.0 Safari/537.36',
    browser: 'Chrome 141',
    platform: 'macOS',
    formFactor: 'desktop',
    proposedName: 'Chrome 141 on macOS – Desktop',
  },
  {
    label: 'Chrome on Android phone',
    userAgent: 'Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) '
      + 'Chrome/141.0.0.0 Mobile Safari/537.36',
    browser: 'Chrome 141',
    platform: 'Android',
    formFactor: 'phone',
    proposedName: 'Chrome 141 on Android – Phone',
  },
  {
    label: 'Chrome on Android tablet',
    userAgent: 'Mozilla/5.0 (Linux; Android 14; SM-X200) AppleWebKit/537.36 (KHTML, like Gecko) '
      + 'Chrome/141.0.0.0 Safari/537.36',
    browser: 'Chrome 141',
    platform: 'Android',
    formFactor: 'tablet',
    proposedName: 'Chrome 141 on Android – Tablet',
  },
  {
    label: 'Edge on Windows',
    userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) '
      + 'Chrome/141.0.0.0 Safari/537.36 Edg/141.0.0.0',
    browser: 'Edge 141',
    platform: 'Windows',
    formFactor: 'desktop',
    proposedName: 'Edge 141 on Windows – Desktop',
  },
  {
    label: 'Firefox on Windows',
    userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:133.0) Gecko/20100101 Firefox/133.0',
    browser: 'Firefox 133',
    platform: 'Windows',
    formFactor: 'desktop',
    proposedName: 'Firefox 133 on Windows – Desktop',
  },
  {
    label: 'Firefox on macOS',
    userAgent: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10.15; rv:133.0) Gecko/20100101 Firefox/133.0',
    browser: 'Firefox 133',
    platform: 'macOS',
    formFactor: 'desktop',
    proposedName: 'Firefox 133 on macOS – Desktop',
  },
  {
    label: 'Safari on macOS',
    userAgent: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) '
      + 'Version/17.6 Safari/605.1.15',
    browser: 'Safari 605',
    platform: 'macOS',
    formFactor: 'desktop',
    proposedName: 'Safari 605 on macOS – Desktop',
  },
  {
    label: 'Safari on iPhone',
    userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_6 like Mac OS X) AppleWebKit/605.1.15 '
      + '(KHTML, like Gecko) Version/17.6 Mobile/15E148 Safari/604.1',
    browser: 'Safari 604',
    platform: 'iOS',
    formFactor: 'phone',
    proposedName: 'Safari 604 on iOS – Phone',
  },
  {
    label: 'Safari on iPad (legacy UA carrying "iPad")',
    userAgent: 'Mozilla/5.0 (iPad; CPU OS 17_6 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) '
      + 'Version/17.6 Mobile/15E148 Safari/604.1',
    browser: 'Safari 604',
    platform: 'iPadOS',
    formFactor: 'tablet',
    proposedName: 'Safari 604 on iPadOS – Tablet',
  },
  {
    label: 'Safari on iPad (modern UA presenting as Macintosh + touch)',
    userAgent: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) '
      + 'Version/17.6 Safari/605.1.15',
    maxTouchPoints: 5,
    browser: 'Safari 605',
    platform: 'iPadOS',
    formFactor: 'tablet',
    proposedName: 'Safari 605 on iPadOS – Tablet',
  },
  {
    label: 'Opera on Windows',
    userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) '
      + 'Chrome/119.0.0.0 Safari/537.36 OPR/105.0.0.0',
    browser: 'Opera 105',
    platform: 'Windows',
    formFactor: 'desktop',
    proposedName: 'Opera 105 on Windows – Desktop',
  },
  {
    label: 'Samsung Internet on Android',
    userAgent: 'Mozilla/5.0 (Linux; Android 14; SM-S918B) AppleWebKit/537.36 (KHTML, like Gecko) '
      + 'SamsungBrowser/26.0 Chrome/122.0.0.0 Mobile Safari/537.36',
    browser: 'Samsung Internet 26',
    platform: 'Android',
    formFactor: 'phone',
    proposedName: 'Samsung Internet 26 on Android – Phone',
  },
  {
    label: 'Firefox on iOS (FxiOS)',
    userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_6 like Mac OS X) AppleWebKit/605.1.15 '
      + '(KHTML, like Gecko) FxiOS/133.0 Mobile/15E148 Safari/605.1.15',
    browser: 'Firefox 133',
    platform: 'iOS',
    formFactor: 'phone',
    proposedName: 'Firefox 133 on iOS – Phone',
  },
  {
    label: 'Chrome on iOS (CriOS)',
    userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_6 like Mac OS X) AppleWebKit/605.1.15 '
      + '(KHTML, like Gecko) CriOS/131.0.6778.73 Mobile/15E148 Safari/605.1.15',
    browser: 'Chrome 131',
    platform: 'iOS',
    formFactor: 'phone',
    proposedName: 'Chrome 131 on iOS – Phone',
  },
  {
    label: 'garbage input',
    userAgent: 'this is not a user agent at all',
    browser: null,
    platform: null,
    formFactor: 'desktop',
    proposedName: 'Desktop',
  },
];

describe('user-agent.util', () => {
  for (const c of CASES) {
    describe(c.label, () => {
      it('parses browser, platform, form factor and proposed name', () => {
        expect(parseBrowser(c.userAgent)).toBe(c.browser);
        expect(parsePlatform(c.userAgent, c.userAgentData, c.maxTouchPoints)).toBe(c.platform);
        expect(parseFormFactor(c.userAgent, c.userAgentData, c.maxTouchPoints)).toBe(c.formFactor);
        expect(buildProposedName(c.userAgent, c.userAgentData, c.maxTouchPoints)).toBe(c.proposedName);
      });
    });
  }

  describe('edge cases', () => {
    it('never throws on empty or null-ish input', () => {
      expect(parseBrowser('')).toBeNull();
      expect(parseBrowser(null)).toBeNull();
      expect(parseBrowser(undefined)).toBeNull();
      expect(parsePlatform(null)).toBeNull();
      expect(parseFormFactor(null)).toBe('desktop');
      expect(buildProposedName(null)).toBe('Desktop');
    });

    it('prefers userAgentData.platform over user agent sniffing when present', () => {
      const ua = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/141.0.0.0 Safari/537.36';
      expect(parsePlatform(ua, { platform: 'Chrome OS' })).toBe('Chrome OS');
    });

    it('treats userAgentData.mobile === true as a phone regardless of the user agent string', () => {
      const ua = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/141.0.0.0 Safari/537.36';
      expect(parseFormFactor(ua, { mobile: true })).toBe('phone');
    });

    it('falls back to just the platform when the browser is unrecognized', () => {
      expect(buildProposedName('Windows NT 10.0 something unrecognized')).toBe('Windows – Desktop');
    });

    it('falls back to just the browser when the platform is unrecognized', () => {
      expect(buildProposedName('Chrome/141.0.0.0 Safari/537.36')).toBe('Chrome 141 – Desktop');
    });
  });
});
