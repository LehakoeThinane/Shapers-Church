// Fails if app code hard-codes a colour. Colours come from @shapers/tokens only (design/tokens/tokens.json).
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';

const roots = ['admin/src', 'mobile/src', 'web/src'];
const pattern = /#[0-9a-fA-F]{3,8}\b|\brgba?\s*\(|\bhsla?\s*\(/;
const problems = [];

function walk(dir) {
  for (const entry of readdirSync(dir)) {
    const path = join(dir, entry);
    if (statSync(path).isDirectory()) walk(path);
    else if (/\.(tsx?|css)$/.test(entry)) {
      readFileSync(path, 'utf8').split('\n').forEach((line, i) => {
        if (pattern.test(line) && !line.includes('colour-check: allow')) problems.push(`${relative('.', path)}:${i + 1}: ${line.trim()}`);
      });
    }
  }
}

roots.forEach(walk);
if (problems.length) {
  console.error('Hard-coded colours found. Use theme tokens instead:\n' + problems.join('\n'));
  process.exit(1);
}
console.log('No hard-coded colours.');
