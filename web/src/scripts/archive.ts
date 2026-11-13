import { hasEnded } from '../lib/archive';

// Applies the archive rule (lib/archive.ts) to a page that was built before something on it ended.
//   data-ends="<ISO>"            the item's end time
//   data-archive="remove"        upcoming-only items (event lists) disappear once ended
//   otherwise                    the item stays as a past highlight: its booking actions go and
//                                [data-past-label] swaps in its past wording
//   data-hide-when-empty         a group hidden once none of its items are left
//   data-show-when-empty         shown in place of an emptied list
for (const el of document.querySelectorAll<HTMLElement>('[data-ends]')) {
  if (!hasEnded(el.dataset.ends!)) continue;
  if (el.dataset.archive === 'remove') {
    el.remove();
    continue;
  }
  el.classList.add('past');
  el.querySelectorAll('[data-booking]').forEach((b) => b.remove());
  el.querySelectorAll<HTMLElement>('[data-past-label]').forEach((label) => (label.textContent = label.dataset.pastLabel!));
}

for (const group of document.querySelectorAll<HTMLElement>('[data-hide-when-empty]')) {
  if (group.querySelector('[data-ends]')) continue;
  group.hidden = true;
  group.parentElement?.querySelector<HTMLElement>('[data-show-when-empty]')?.removeAttribute('hidden');
}

// A heading that mentions what's coming up switches to its past wording once nothing in its section is.
for (const el of document.querySelectorAll<HTMLElement>('[data-past-heading]')) {
  if (!el.closest('section')?.querySelector('[data-ends]:not(.past)')) el.textContent = el.dataset.pastHeading!;
}
