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
					label: 'Introduction',
					items: [
						'introduction/getting-started',
						'introduction/quickstart',
						'introduction/first-action',
						'introduction/manual-setup',
						'introduction/samples-and-template',
					],
				},
				{
					label: 'SDK',
					items: [
						{ label: 'Overview', slug: 'sdk' },
						'sdk/hosting',
						'sdk/capabilities',
						'sdk/variables',
						'sdk/flows',
						'sdk/devices',
						'sdk/layouts',
						'sdk/localization',
						'sdk/logging',
						'sdk/authentication',
						'sdk/testing',
						'sdk/conformance',
						'sdk/analyzers',
						'sdk/capability-parity',
					],
				},
				{
					label: 'Macro Deck UI',
					items: [
						{ label: 'Overview', slug: 'sdk/ui' },
						{
							label: 'Concepts',
							collapsed: true,
							items: [
								'sdk/ui/concepts/ui-model',
								'sdk/ui/concepts/state-and-bindings',
								'sdk/ui/concepts/events',
								'sdk/ui/concepts/reactive-updates',
								'sdk/ui/concepts/sizing',
								'sdk/ui/concepts/theming',
							],
						},
						{
							label: 'Components',
							collapsed: true,
							items: [
								{ label: 'All components', slug: 'sdk/ui/components' },
								'sdk/ui/components/stack-and-layer',
								'sdk/ui/components/transform',
								'sdk/ui/components/list',
								'sdk/ui/components/text',
								'sdk/ui/components/image',
								'sdk/ui/components/button',
								'sdk/ui/components/slider',
								'sdk/ui/components/text-field',
								'sdk/ui/components/range-bar',
								'sdk/ui/components/chart',
								'sdk/ui/components/time',
								'sdk/ui/components/progress',
							],
						},
						{
							label: 'Views',
							collapsed: true,
							items: [
								{ label: 'Views and surfaces', slug: 'sdk/ui/views' },
								'sdk/ui/views/sessions',
								'sdk/ui/views/configuration',
								'sdk/ui/views/widget',
								'sdk/ui/views/widget-configuration',
								{ label: 'Widget types', slug: 'sdk/widgets' },
								{ label: 'Folder view', slug: 'sdk/folder-views' },
								'sdk/ui/views/modal',
								'sdk/ui/views/developer-preview',
								'sdk/ui/views/custom',
							],
						},
						{
							label: 'Reference',
							collapsed: true,
							items: [
								'sdk/ui/reference/patches',
								'sdk/ui/reference/resources',
								'sdk/ui/reference/compatibility',
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
						'reference/manifest',
						'reference/protocol',
						'reference/websocket',
						...openAPISidebarGroups,
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
