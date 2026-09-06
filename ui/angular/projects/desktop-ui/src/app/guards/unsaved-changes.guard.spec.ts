import { ConfirmsNavigation, unsavedChangesGuard } from './unsaved-changes.guard';

function run(component: Partial<ConfirmsNavigation> | null): Promise<boolean> | boolean {
  return unsavedChangesGuard(
    component as Partial<ConfirmsNavigation>,
    null as never,
    null as never,
    null as never,
  ) as Promise<boolean> | boolean;
}

describe('unsavedChangesGuard', () => {
  it('lets a component without the hook leave', () => {
    expect(run({})).toBeTrue();
    expect(run(null)).toBeTrue();
  });

  it('forwards the answer the component gave', () => {
    expect(run({ confirmNavigation: () => false })).toBeFalse();
    expect(run({ confirmNavigation: () => true })).toBeTrue();
  });

  it('forwards a pending answer unchanged, so the router waits for the dialog', async () => {
    const pending = Promise.resolve(true);
    expect(run({ confirmNavigation: () => pending })).toBe(pending);
    await expectAsync(pending).toBeResolvedTo(true);
  });
});
