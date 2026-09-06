import { EXPORT_PASSWORD_MIN_LENGTH, GENERATED_EXPORT_PASSWORD_LENGTH, assessExportPassword, generateExportPassword } from './export-password.util';
import { bundledTranslator as t } from '../../testing/localization-test-support';

describe('export-password.util', () => {
  describe('generateExportPassword', () => {
    it('returns the requested length', () => {
      expect(generateExportPassword().length).toBe(GENERATED_EXPORT_PASSWORD_LENGTH);
      expect(generateExportPassword(40).length).toBe(40);
    });

    it('never emits ambiguous characters', () => {
      const sample = Array.from({ length: 50 }, () => generateExportPassword()).join('');

      expect(sample).not.toMatch(/[O0Il1]/);
    });

    it('does not repeat itself', () => {
      const drawn = new Set(Array.from({ length: 50 }, () => generateExportPassword()));

      expect(drawn.size).toBe(50);
    });

    it('clears the minimum length by a wide margin', () => {
      expect(assessExportPassword(generateExportPassword(), t).strength).toBe('strong');
    });

    it('draws uniformly across the alphabet', () => {
      const sample = Array.from({ length: 400 }, () => generateExportPassword(50)).join('');
      const early = [...sample].filter(character => 'abcdefghijkmnopqrstuvwxy'.includes(character)).length;
      const share = early / sample.length;

      expect(share).toBeGreaterThan(0.4);
      expect(share).toBeLessThan(0.48);
    });
  });

  describe('assessExportPassword', () => {
    it('rejects anything under the shared minimum', () => {
      const assessment = assessExportPassword('a'.repeat(EXPORT_PASSWORD_MIN_LENGTH - 1), t);

      expect(assessment.acceptable).toBeFalse();
      expect(assessment.strength).toBe('too-short');
    });

    it('accepts the minimum length itself', () => {
      expect(assessExportPassword('a'.repeat(EXPORT_PASSWORD_MIN_LENGTH), t).acceptable).toBeTrue();
    });

    it('rates a short single-class password weak', () => {
      expect(assessExportPassword('password', t).strength).toBe('weak');
    });

    it('rewards length over punctuation', () => {
      expect(assessExportPassword('correcthorsebatterystaple', t).strength).toBe('strong');
      expect(assessExportPassword('P@ss1!', t).acceptable).toBeFalse();
    });
  });
});
