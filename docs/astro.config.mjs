// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';
import mermaid from 'astro-mermaid';
import starlightLinksValidator from 'starlight-links-validator';
import starlightOpenAPI, { openAPISidebarGroups } from 'starlight-openapi';

// Where this documentation and the code it describes actually live. Used for
// "Edit this page" and for every link into a source file.
const repo = 'https://github.com/Macro-Deck-App/Macro-Deck';

// The project's public landing repository, which is what the header links to.
const projectRepo = 'https://github.com/Macro-Deck-App/Macro-Deck';

export default defineConfig({
	site: 'https://docs.macro-deck.app',
	// No `base`: the site is served from the root of its own domain.
	integrations: [
		// Must precede Starlight so ```mermaid fences are transformed before rendering.
		mermaid(),
		starlight({
			title: 'Macro Deck Developer Docs',
			description:
				'Build, test, package and publish plugins for Macro Deck 3.',
			logo: { src: './src/assets/macro-deck-logo.svg', alt: 'Macro Deck' },
			// PNG rather than the SVG: the full icon's glow layers are raster, so its
			// vector form is far too heavy to serve as a favicon.
			favicon: '/favicon-32.png',
			head: [
				{ tag: 'link', attrs: { rel: 'icon', type: 'image/png', sizes: '192x192', href: '/favicon-192.png' } },
				{ tag: 'link', attrs: { rel: 'apple-touch-icon', href: '/apple-touch-icon.png' } },
			],
			customCss: ['./src/styles/theme.css'],
			components: { Footer: './src/components/Footer.astro' },
			social: [
				{ icon: 'github', label: 'GitHub', href: projectRepo },
				{ icon: 'discord', label: 'Discord', href: 'https://discord.macro-deck.app' },
				// Starlight ships no Ko-fi icon; heart is its generic support mark.
				{ icon: 'heart', label: 'Donate', href: 'https://ko-fi.com/manuelmayer' },
			],
			editLink: { baseUrl: `${repo}/edit/main/docs/` },
			plugins: [
				// Renders the hand-authored plugin protocol REST surface under /reference/rest/.
				starlightOpenAPI([
					{
						base: 'reference/rest',
						label: 'REST API',
						schema: './public/specs/openapi.yaml',
					},
				]),
				starlightLinksValidator({
					exclude: [
						// Static downloads, not pages.
						'/specs/**',
						'/schemas/**',
						// Injected by starlight-openapi, so not in the content collection
						// the validator walks.
						'/reference/rest/**',
					],
				}),
			],
			sidebar: [
				{
					label: 'Get started',
					items: [
						'introduction/quickstart',
						'introduction/first-action',
						{ label: 'Project setup', slug: 'introduction/manual-setup' },
						'introduction/samples-and-template',
					],
				},
				{
					label: 'Features',
					items: [
						{ label: 'Overview', slug: 'features' },
						'features/actions',
						'features/button-states',
						'features/button-icons',
						'features/variables',
						'features/events',
						'features/setup-flows',
						'features/music-players',
						'features/weather',
						'features/virtual-profiles',
						{ label: 'Devices', slug: 'features/devices' },
						{ label: 'Layouts', slug: 'features/layouts' },
						{ label: 'Widget types', link: '/ui/views/widget-types/' },
						{ label: 'Folder views', link: '/ui/views/folder-views/' },
						'features/integration-issues',
						'features/settings-migrations',
						'features/localization',
						{ label: 'Logging', slug: 'features/logging' },
						{ label: 'Testing', slug: 'features/testing' },
					],
				},
				{
					label: 'Macro Deck UI',
					items: [
						{ label: 'Overview', slug: 'ui' },
						{
							label: 'Components',
							collapsed: true,
							items: [
								{ label: 'All components', slug: 'ui/components' },
								'ui/components/stack-and-layer',
								'ui/components/transform',
								'ui/components/list',
								'ui/components/text',
								'ui/components/image',
								'ui/components/button',
								'ui/components/slider',
								'ui/components/text-field',
								'ui/components/range-bar',
								'ui/components/chart',
								'ui/components/time',
								'ui/components/progress',
							],
						},
						{
							label: 'Views',
							collapsed: true,
							items: [
								{ label: 'Views and surfaces', slug: 'ui/views' },
								'ui/views/sessions',
								'ui/views/configuration',
								'ui/views/widget',
								'ui/views/widget-configuration',
								{ label: 'Widget types', slug: 'ui/views/widget-types' },
								{ label: 'Folder view', slug: 'ui/views/folder-views' },
								'ui/views/modal',
								'ui/views/developer-preview',
								'ui/views/custom',
							],
						},
						{
							label: 'Concepts',
							collapsed: true,
							items: [
								'ui/concepts/ui-model',
								'ui/concepts/state-and-bindings',
								'ui/concepts/events',
								'ui/concepts/reactive-updates',
								'ui/concepts/sizing',
								'ui/concepts/theming',
							],
						},
						{
							label: 'Reference',
							collapsed: true,
							items: [
								'ui/reference/patches',
								'ui/reference/resources',
								'ui/reference/compatibility',
							],
						},
					],
				},
				{
					label: 'CLI',
					items: [
						{ label: 'Overview', slug: 'cli' },
						{ label: 'new', slug: 'cli/new' },
						{ label: 'build', slug: 'cli/build' },
						{ label: 'validate', slug: 'cli/validate' },
						{ label: 'inspect', slug: 'cli/inspect' },
						{ label: 'pack', slug: 'cli/pack' },
						{ label: 'run', slug: 'cli/run' },
						{ label: 'test', slug: 'cli/test' },
						'cli/signing',
						'cli/ci',
					],
				},
				{
					label: 'Guides',
					items: [
						'guides/debugging',
						'guides/publishing',
						'guides/troubleshooting',
					],
				},
				{
					label: 'Reference',
					items: [
						{ label: 'SDK packages', slug: 'reference/sdk-packages' },
						'reference/plugin-hosting',
						'reference/manifest',
						'reference/capability-parity',
						'reference/authentication',
						'reference/protocol',
						'reference/websocket',
						...openAPISidebarGroups,
						'reference/conformance',
						'reference/analyzers',
					],
				},
				{
					label: 'Policies',
					items: [
						'policies/compatibility',
						'policies/deprecations',
						'policies/migrations',
						'policies/security',
					],
				},
			],
		}),
	],
});
