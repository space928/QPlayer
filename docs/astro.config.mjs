// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';
import starlightAutoSidebar from 'starlight-auto-sidebar';
import starlightGiscus from 'starlight-giscus';
import starlightKbd from 'starlight-kbd'

// https://astro.build/config
export default defineConfig({
	site: 'https://space928.github.io',
	base: '/QPlayer',
	integrations: [
		starlight({
			title: 'QPlayer Documentation',
			plugins: [
				starlightAutoSidebar(),
				starlightGiscus({
					repo: 'space928/QPlayer',
					repoId: 'R_kgDOLSwoIA',
					category: 'Comments',
					categoryId: 'DIC_kwDOLSwoIM4CobZs',
					mapping: '<title>',
					reactions: true,
					lazy: true
				}),
				starlightKbd({
					types: [
						{ id: 'mac', label: 'macOS' },
						{ id: 'windows', label: 'Windows', default: true },
					],
				}),
			],
			logo: {
				src: './src/assets/Splash.png',
				replacesTitle: true,
			},
			social: {
				github: 'https://github.com/space928/QPlayer',
			},
			sidebar: [
				{
					label: 'Guides',
					items: [
						// Each item here is one entry in the navigation menu.
						{ label: 'Getting Started', slug: 'guides/getting-started' },
					],
				},
				{
					label: 'Reference',
					autogenerate: { directory: 'reference' },
				},
			],
			customCss: [
				// Relative path to your custom CSS file
				'./src/styles/custom.css',
			],
		}),
	],
});
