// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';
import mermaid from 'astro-mermaid';
import starlightLinksValidator from 'starlight-links-validator';
import starlightOpenAPI, { openAPISidebarGroups } from 'starlight-openapi';
import starlightSidebarTopics from 'starlight-sidebar-topics';

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
			title: 'Macro Deck Docs',
			description:
				'Install and use Macro Deck 3, and build, test, package and publish plugins for it.',
			logo: { src: './src/assets/macro-deck-logo.svg', alt: 'Macro Deck' },
			// PNG rather than the SVG: the full icon's glow layers are raster, so its
			// vector form is far too heavy to serve as a favicon.
			favicon: '/favicon-32.png',
			head: [
				{ tag: 'link', attrs: { rel: 'icon', type: 'image/png', sizes: '192x192', href: '/favicon-192.png' } },
				{ tag: 'link', attrs: { rel: 'apple-touch-icon', href: '/apple-touch-icon.png' } },
			],
			customCss: ['./src/styles/theme.css'],
			components: {
				Footer: './src/components/Footer.astro',
				SiteTitle: './src/components/SiteTitle.astro',
			},
			social: [
				{ icon: 'github', label: 'GitHub', href: projectRepo },
				{ icon: 'discord', label: 'Discord', href: 'https://discord.macro-deck.app' },
				// The website's donation page offers GitHub Sponsors and Ko-fi.
				{ icon: 'heart', label: 'Donate', href: 'https://macro-deck.app/donate' },
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
				starlightSidebarTopics(
					[
						{
							id: 'guide',
							label: 'User guide',
							link: '/guide/',
							icon: 'open-book',
							items: [
								{
									label: 'Get started',
									items: [
										{ label: 'Overview', slug: 'guide' },
										'guide/installation',
										'guide/getting-started',
										'guide/usb-connection',
										'guide/companion-app',
									],
								},
								{
									label: 'Using Macro Deck',
									items: ['guide/concepts', 'guide/tips', 'guide/updates', 'guide/backups', 'guide/troubleshooting'],
								},
								{
									label: 'Reference',
									items: ['guide/compatibility', 'guide/recommended-devices'],
								},
							],
						},
						{
							id: 'developers',
							label: 'Plugin development',
							link: '/introduction/quickstart/',
							icon: 'puzzle',
							items: [
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
										{ label: 'Messaging between plugins', slug: 'features/messaging' },
										'features/deck',
										'features/setup-flows',
										'features/music-players',
										'features/weather',
										'features/virtual-profiles',
										{ label: 'Devices', slug: 'features/devices' },
										{ label: 'Layouts', slug: 'features/layouts' },
										{ label: 'Android devices', slug: 'features/android-devices' },
										{ label: 'Widget types', link: '/ui/views/widget-types/' },
										{ label: 'Folder views', link: '/ui/views/folder-views/' },
										{ label: 'Screensavers', link: '/ui/views/screensavers/' },
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
												'ui/components/grid',
												'ui/components/transform',
												'ui/components/modifier',
												'ui/components/responsive',
												'ui/components/list',
												'ui/components/text',
												'ui/components/image',
												'ui/components/icon',
												'ui/components/shape',
												'ui/components/button',
												'ui/components/toggle',
												'ui/components/segmented',
												'ui/components/slider',
												'ui/components/dial',
												'ui/components/text-field',
												'ui/components/range-bar',
												'ui/components/gauge',
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
												{ label: 'Screensaver', slug: 'ui/views/screensavers' },
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
										{ label: 'merge', slug: 'cli/merge' },
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
						},
						{
							id: 'creator-portal',
							label: 'Creator Portal',
							link: '/creator-portal/',
							icon: 'rocket',
							items: [
								{
									label: 'Get started',
									items: [
										{ label: 'Overview', slug: 'creator-portal' },
										'creator-portal/projects',
									],
								},
								{
									label: 'Publish',
									items: [
										'creator-portal/publish-plugin',
										'creator-portal/publish-icon-pack',
										'creator-portal/review',
										'creator-portal/conformance',
										'creator-portal/testers',
									],
								},
								{
									label: 'Reference',
									items: ['creator-portal/release-workflow'],
								},
							],
						},
					],
					// starlight-openapi injects these pages, so they are listed in no topic's items.
					{ topics: { developers: ['/reference/rest/**'] } },
				),
			],
		}),
	],
});
