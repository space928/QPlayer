# QPlayer Documentation

This project contains the source files used to build the documentation for QPlayer.

[![Built with Starlight](https://astro.badg.es/v2/built-with-starlight/tiny.svg)](https://starlight.astro.build)

## Project Structure

```
.
├── public/
├── src/
│   ├── assets/
│   ├── content/
│   │   ├── docs/
│   └── content.config.ts
├── astro.config.mjs
├── package.json
└── tsconfig.json
```

Starlight (the documentation build system) looks for `.md` or `.mdx` files in the 
`src/content/docs/` directory. Each file is exposed as a route based on its file 
name.

Images can be added to `src/assets/` and embedded in Markdown with a relative link.

Static assets, like favicons, can be placed in the `public/` directory.

## Commands

All commands are run from the root of the project, from a terminal:

| Command                   | Action                                           |
| :------------------------ | :----------------------------------------------- |
| `npm install`             | Installs dependencies                            |
| `npm run dev`             | Starts local dev server at `localhost:4321`      |
| `npm run build`           | Build the documentation site to `./dist/`        |
| `npm run preview`         | Preview the built docs locally, before deploying |
| `npm run astro ...`       | Run CLI commands like `astro add`, `astro check` |
| `npm run astro -- --help` | Get help using the Astro CLI                     |

Documentation for the build system:
 - [Starlight](https://starlight.astro.build/)
 - [Astro](https://docs.astro.build)
