import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

// A device-specific web-client build (issue #727) is described in several places that have to
// agree: the target manifest, the build that emits it, the two steps that copy the artifact into
// wwwroot, and the host route that serves it. Nothing fails loudly when they drift - the target
// just serves the default client, or 404s - so the agreement is pinned here rather than reviewed.
const repoRoot = new URL('../../', import.meta.url);

function read(relativePath) {
	return readFileSync(fileURLToPath(new URL(relativePath, repoRoot)), 'utf8');
}

function directoriesIn(relativePath) {
	return readdirSync(fileURLToPath(new URL(relativePath, repoRoot)), { withFileTypes: true })
		.filter(entry => entry.isDirectory())
		.map(entry => entry.name);
}

/** A target is a directory of `src/targets/` holding the module the build swaps in. */
const targets = directoriesIn('ui/web-client/src/targets')
	.filter(name => existsSync(fileURLToPath(
		new URL(`ui/web-client/src/targets/${name}/active-target.ts`, repoRoot))));

test('the client defines at least one device target', () => {
	assert.ok(targets.length > 0,
		'issue #727 ships the Car Thing target; a client with none means the manifest was lost');
});

for (const id of targets) {
	test(`${id}: the id is a single lower-case path segment`, () => {
		// The host derives its route template and file path from the directory name; anything else
		// either fails WebClientTargets.IsValidId or introduces a path segment.
		assert.match(id, /^[a-z0-9][a-z0-9-]{0,31}$/);
	});

	test(`${id}: the manifest names itself the same as the directory it lives in`, () => {
		// The build takes the id from the flag and the directory; the manifest carries it again for
		// the host to identify the device by. Two names for one target is a target that half works.
		const manifest = read(`ui/web-client/src/targets/${id}/${id}.target.ts`);
		assert.match(manifest, new RegExp(`id:\\s*'${id}'`),
			`the ${id} manifest does not declare that id`);
	});

	test(`${id}: an npm script produces the artifact`, () => {
		const scripts = JSON.parse(read('ui/web-client/package.json')).scripts;
		const script = Object.values(scripts).find(command => command.includes(`--target=${id}`));
		assert.ok(script, `no script builds --target=${id}, so its dist is copied from nothing`);
	});

	test(`${id}: both packaging steps copy the artifact to the path the host serves`, () => {
		const destination = `wwwroot/targets/${id}`;

		const workflow = read('.github/workflows/build.yml');
		assert.ok(workflow.includes(`cp -R ui/web-client/dist-${id} publish/${destination}`),
			`build.yml does not copy dist-${id} to ${destination}`);

		const staging = read('ci/scripts/stage-host.sh');
		assert.ok(staging.includes(`ui/web-client/dist-${id}`),
			`stage-host.sh does not stage dist-${id}`);
		assert.ok(staging.includes(`"$publish_dir/${destination}"`),
			`stage-host.sh does not stage ${destination}`);
	});

	test(`${id}: CI builds the target, so a broken one is not found by a device`, () => {
		const workflow = read('.github/workflows/ci.yml');
		const scripts = JSON.parse(read('ui/web-client/package.json')).scripts;
		const scriptName = Object.keys(scripts).find(name => scripts[name].includes(`--target=${id}`));
		assert.ok(workflow.includes(`npm run ${scriptName}`),
			`ci.yml never runs ${scriptName}`);
	});
}

test('the build gives a target shell the base href the host serves it from', () => {
	// A mismatch resolves every asset against the wrong root, which loads nothing at all.
	const build = read('ui/web-client/build.mjs');
	assert.match(build, /\/targets\/\$\{targetId\}\//,
		'build.mjs no longer derives the base href from the target id');
});

test('the service worker leaves a target navigation to the host', () => {
	// The worker's scope is the origin root, so without this it would answer a target's navigation
	// with the default shell (ADR 0041). The decision is executed rather than matched for,
	// because a substring check passes on a worker that mentions targets and answers them anyway.
	const worker = read('ui/web-client/src/pwa/service-worker.js');
	const segments = /var FOREIGN_SEGMENTS = \[[^\]]*\];/.exec(worker);
	const routine = /function isForeignRoute\(pathname\) \{[\s\S]*?\n\}/.exec(worker);
	assert.ok(segments && routine, 'the worker no longer decides foreign routes in one place');

	const isForeignRoute = new Function(`${segments[0]}\n${routine[0]}\nreturn isForeignRoute;`)();
	for (const id of targets) {
		assert.equal(isForeignRoute(`/targets/${id}/`), true,
			`the worker would answer the ${id} target's navigation with the default shell`);
	}
	assert.equal(isForeignRoute('/admin'), true, 'the worker would answer the configuration UI');
	assert.equal(isForeignRoute('/'), false, 'the worker no longer serves the deck it exists for');
});
