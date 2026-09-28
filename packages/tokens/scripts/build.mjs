// Generates the theme tokens used by the apps from design/tokens/tokens.json.
//   node scripts/build.mjs          write src/tokens.generated.ts and css/tokens.css
//   node scripts/build.mjs --check  fail if the generated files are out of date (used in CI)
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const source = resolve(here, '../../../design/tokens/tokens.json');
const tsOut = resolve(here, '../src/tokens.generated.ts');
const cssOut = resolve(here, '../css/tokens.css');
const header = 'Generated from design/tokens/tokens.json by `pnpm tokens`. Do not edit by hand.';

const tokens = JSON.parse(readFileSync(source, 'utf8'));

/** Drops $description keys and unwraps { $value } leaves. */
function unwrap(node) {
  if (node && typeof node === 'object' && !Array.isArray(node)) {
    if ('$value' in node) return node.$value;
    return Object.fromEntries(
      Object.entries(node)
        .filter(([key]) => !key.startsWith('$'))
        .map(([key, value]) => [key, unwrap(value)]),
    );
  }
  return node;
}

const palettes = unwrap(tokens.palettes);
const shared = {
  font: unwrap(tokens.font),
  radius: unwrap(tokens.radius),
  space: unwrap(tokens.space),
  blur: unwrap(tokens.blur),
};

// ---------- TypeScript ----------
const ts = [
  `// ${header}`,
  '',
  `export const palettes = ${JSON.stringify(palettes, null, 2)} as const;`,
  '',
  ...Object.entries(shared).map(([name, value]) => `export const ${name} = ${JSON.stringify(value, null, 2)} as const;\n`),
].join('\n');

// ---------- CSS ----------
const kebab = (s) => s.replace(/[A-Z]/g, (c) => `-${c.toLowerCase()}`);

function flatten(prefix, node, out = []) {
  for (const [key, value] of Object.entries(node)) {
    const name = `${prefix}-${kebab(key)}`;
    if (Array.isArray(value)) continue;
    if (value && typeof value === 'object') flatten(name, value, out);
    else out.push([name, value]);
  }
  return out;
}

const rimStops = [0, 30, 55, 80, 100];
function paletteBlock(palette) {
  const lines = flatten('--color', palette.color).map(([name, value]) => `  ${name}: ${value};`);
  const rim = palette.color.rim;
  lines.push(
    rim.length
      ? `  --color-rim: linear-gradient(135deg, ${rim.map((c, i) => `${c} ${rimStops[i] ?? 100}%`).join(', ')});`
      : '  --color-rim: none;',
  );
  lines.push(`  color-scheme: ${palette.appearance};`);
  return lines.join('\n');
}

const sharedCss = [
  ...Object.entries(shared.radius).map(([k, v]) => `  --radius-${kebab(k)}: ${v}px;`),
  ...Object.entries(shared.space).map(([k, v]) => `  --space-${kebab(k)}: ${v}px;`),
  `  --blur-glass: ${shared.blur.glass}px;`,
  `  --font-ui: ${shared.font.ui};`,
  `  --font-serif: ${shared.font.serif};`,
].join('\n');

const css = `/* ${header} */

:root {
${sharedCss}
}

/* Midnight is the default; Rose applies when chosen, or when the system is light and no palette is set. */
:root,
:root[data-palette="midnight"] {
${paletteBlock(palettes.midnight)}
}

:root[data-palette="rose"] {
${paletteBlock(palettes.rose)}
}

@media (prefers-color-scheme: light) {
  :root:not([data-palette]) {
${paletteBlock(palettes.rose).replace(/^ {2}/gm, '    ')}
  }
}
`;

const outputs = [
  [tsOut, ts],
  [cssOut, css],
];

if (process.argv.includes('--check')) {
  const stale = outputs.filter(([file, content]) => !existsSync(file) || readFileSync(file, 'utf8') !== content);
  if (stale.length) {
    console.error(`Tokens are out of date: ${stale.map(([f]) => f).join(', ')}\nRun: pnpm tokens`);
    process.exit(1);
  }
  console.log('Tokens are up to date.');
} else {
  for (const [file, content] of outputs) writeFileSync(file, content);
  console.log('Wrote', outputs.map(([f]) => f).join(', '));
}
