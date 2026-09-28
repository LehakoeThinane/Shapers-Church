// Checks the palettes against WCAG 2.2 contrast minimums so a token edit can't quietly break readability.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const tokens = JSON.parse(readFileSync(new URL('../../../design/tokens/tokens.json', import.meta.url), 'utf8'));
const color = (palette, path) => path.split('.').reduce((node, key) => node[key], tokens.palettes[palette].color).$value;

function luminance(hex) {
  const channels = hex.replace('#', '').match(/../g).map((h) => parseInt(h, 16) / 255);
  const [r, g, b] = channels.map((c) => (c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

function contrast(a, b) {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

const TEXT = 4.5; // normal text
const UI = 3; // icons, active tab, large text

const pairs = [
  ['text.primary', 'background', TEXT],
  ['text.onAccent', 'accent', TEXT],
  ['text.onButton', 'button.primaryTop', TEXT],
  ['text.onButton', 'button.primaryBottom', TEXT],
  ['interactive', 'background', UI],
  ['danger', 'background', TEXT],
  ['success', 'background', TEXT],
];

// Known, accepted gaps in the locked palettes. Reported on every run rather than failing the build.
// Rose accent (#D2979F) on its background is ~2:1: fine as a fill behind dark text (badges, Give),
// too faint for thin accent-only marks. Use `interactive` for those in Rose.
const advisories = [['accent', 'background', UI]];

for (const palette of Object.keys(tokens.palettes)) {
  for (const [fg, bg, minimum] of advisories) {
    test(`${palette}: ${fg} on ${bg} (advisory, ${minimum}:1)`, (t) => {
      const ratio = contrast(color(palette, fg), color(palette, bg));
      if (ratio < minimum) t.diagnostic(`advisory: ${ratio.toFixed(2)}:1 is below ${minimum}:1`);
    });
  }

  for (const [fg, bg, minimum] of pairs) {
    test(`${palette}: ${fg} on ${bg} meets ${minimum}:1`, () => {
      const ratio = contrast(color(palette, fg), color(palette, bg));
      assert.ok(ratio >= minimum, `${ratio.toFixed(2)}:1 is below ${minimum}:1`);
    });
  }
}
