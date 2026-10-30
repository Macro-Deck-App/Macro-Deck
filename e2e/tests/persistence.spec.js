import { existsSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, useIsolatedHost } from './support/fixtures.js';

useIsolatedHost('persistence', { prepare: api => api.completeOnboarding() });

function documentsDirectory(host) {
  return join(host.dataDir, 'data');
}

function corruptCopies(file) {
  const directory = join(file, '..');
  const name = file.split(/[\\/]/).pop();
  return readdirSync(directory).filter(entry => entry.startsWith(`${name}.corrupt-`)).map(entry => join(directory, entry));
}

// E2E-PER-02: a document damaged while the host was stopped comes back from its backup copy on the
// next start, the damaged file is kept aside, and the user is told about it.
test('a truncated profile and a corrupt user-variables file are recovered from their backups', async ({ api, host }) => {
  test.setTimeout(180_000);
  const profile = await api.profile('E2E Durable Draft');
  await api.put('/api/profiles', { id: profile.id, name: 'E2E Durable' });
  await api.variable('e2e_durable', { value: 'first' });
  await api.setVariable('e2e_durable', 'second');

  const profileFile = join(documentsDirectory(host), 'profiles', `${profile.id}.json`);
  const variablesFile = join(documentsDirectory(host), 'user-variables.json');
  for (const file of [profileFile, variablesFile]) {
    expect(existsSync(file), file).toBe(true);
    expect(existsSync(`${file}.bak`), `${file}.bak`).toBe(true);
  }
  const profileBackup = JSON.parse(readFileSync(`${profileFile}.bak`, 'utf8'));
  const variablesBackup = readFileSync(`${variablesFile}.bak`, 'utf8');
  expect(profileBackup.id ?? profileBackup.Id).toBe(profile.id);

  await host.stop();
  const truncated = readFileSync(profileFile).subarray(0, 10);
  writeFileSync(profileFile, truncated);
  writeFileSync(variablesFile, '{ this is not json');
  await host.start();

  const recovered = (await api.profiles()).find(item => item.id === profile.id);
  expect(recovered).toBeTruthy();
  expect(recovered.name).toBe(profileBackup.name ?? profileBackup.Name);
  const variable = await api.findVariable('e2e_durable');
  expect(variable).toBeTruthy();
  expect(variablesBackup).toContain(variable.value);

  const profileCopies = corruptCopies(profileFile);
  expect(profileCopies).toHaveLength(1);
  expect(readFileSync(profileCopies[0]).equals(truncated)).toBe(true);
  const variableCopies = corruptCopies(variablesFile);
  expect(variableCopies).toHaveLength(1);
  expect(readFileSync(variableCopies[0], 'utf8')).toBe('{ this is not json');

  await expect.poll(async () => JSON.stringify(await api.get('/api/notifications')))
    .toContain('Recovered files after an interrupted write');
});
