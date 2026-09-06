import { applyWidgetDataPatch, diffWidgetData } from './widget-data-patch';

describe('widget data patch', () => {
  describe('diffWidgetData', () => {
    it('reports nothing when the bags match', () => {
      expect(diffWidgetData({ label: 'a' }, { label: 'a' })).toBeNull();
    });

    it('reports only the changed key', () => {
      expect(diffWidgetData({ label: 'a', backgroundColor: '#111' }, { label: 'b', backgroundColor: '#111' }))
        .toEqual({ label: 'b' });
    });

    it('walks into nested state objects', () => {
      const before = { states: { off: { label: 'a', backgroundColor: '#111' }, on: { label: 'x' } } };
      const after = { states: { off: { label: 'b', backgroundColor: '#111' }, on: { label: 'x' } } };

      expect(diffWidgetData(before, after)).toEqual({ states: { off: { label: 'b' } } });
    });

    it('reports a removed key so clearing a property propagates', () => {
      expect(diffWidgetData({ backgroundColor: '#111' }, {})).toEqual({ backgroundColor: undefined });
    });

    it('compares arrays whole rather than per index', () => {
      expect(diffWidgetData({ flows: [{ id: 'a' }] }, { flows: [{ id: 'b' }] }))
        .toEqual({ flows: [{ id: 'b' }] });
    });
  });

  describe('applyWidgetDataPatch', () => {
    it('returns the draft untouched when there is no patch', () => {
      const draft = { label: 'a' };

      expect(applyWidgetDataPatch(draft, null)).toBe(draft);
    });

    it('keeps draft keys the patch does not mention', () => {
      const merged = applyWidgetDataPatch({ label: 'a', backgroundColor: '#unsaved' }, { label: 'from a flow' });

      expect(merged).toEqual({ label: 'from a flow', backgroundColor: '#unsaved' });
    });

    it('merges into nested state objects rather than replacing them', () => {
      const draft = { states: { off: { label: 'a', backgroundColor: '#unsaved' } } };

      const merged = applyWidgetDataPatch(draft, { states: { off: { label: 'from a flow' } } });

      expect(merged).toEqual({ states: { off: { label: 'from a flow', backgroundColor: '#unsaved' } } });
    });

    it('does not mutate the draft it was given', () => {
      const draft = { label: 'a' };

      applyWidgetDataPatch(draft, { label: 'b' });

      expect(draft.label).toBe('a');
    });
  });
});
