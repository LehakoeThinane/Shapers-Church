import type { Schemas } from './api';

/** Splits CSV text into rows of fields, honouring quoted fields with commas, quotes and line breaks. */
export function parseCsv(text: string): string[][] {
  const rows: string[][] = [];
  let row: string[] = [];
  let field = '';
  let quoted = false;
  for (let i = 0; i < text.length; i++) {
    const ch = text[i];
    if (quoted) {
      if (ch === '"' && text[i + 1] === '"') {
        field += '"';
        i++;
      } else if (ch === '"') {
        quoted = false;
      } else {
        field += ch;
      }
    } else if (ch === '"') {
      quoted = true;
    } else if (ch === ',') {
      row.push(field);
      field = '';
    } else if (ch === '\n' || ch === '\r') {
      if (ch === '\r' && text[i + 1] === '\n') i++;
      row.push(field);
      rows.push(row);
      row = [];
      field = '';
    } else {
      field += ch;
    }
  }
  if (field !== '' || row.length > 0) {
    row.push(field);
    rows.push(row);
  }
  return rows.filter((r) => r.some((f) => f.trim() !== ''));
}

// Exports often carry invisible direction marks around phone numbers.
const invisible = /[‎‏‪-‮⁦-⁩﻿]/g;

const splitValues = (value: string | undefined) =>
  (value ?? '')
    .replace(invisible, '')
    .split(';')
    .map((v) => v.trim())
    .filter(Boolean);

const emailColumns = ['Home Email', 'Work Email', 'Other Email', 'Email'];
const phoneColumns = ['Mobile Phone Number', 'Home Phone Number', 'Work Phone Number', 'Other Phone Number', 'Mobile', 'Phone'];

/**
 * Reads a people export (Planning Center's "People Export" columns, or any file with First Name and Last Name
 * columns) into import rows. Values come across as they are; the server checks them.
 */
export function readPeopleCsv(text: string): { rows: Schemas['ImportPersonRow'][]; error?: string } {
  const [header, ...lines] = parseCsv(text.replace(/^﻿/, ''));
  if (!header) return { rows: [], error: 'The file is empty.' };

  const index = new Map(header.map((h, i) => [h.trim().toLowerCase(), i]));
  const col = (line: string[], name: string) => {
    const i = index.get(name.toLowerCase());
    return i === undefined ? undefined : line[i];
  };
  if (!index.has('first name') || !index.has('last name')) {
    return { rows: [], error: 'The file needs "First Name" and "Last Name" columns.' };
  }

  const rows = lines.map((line, i) => {
    const birthdate = (col(line, 'Birthdate') ?? col(line, 'Date of Birth') ?? '').trim().slice(0, 10);
    return {
      // Row 1 is the header, so the first person is on row 2, matching what a spreadsheet shows.
      rowNumber: i + 2,
      firstName: (col(line, 'First Name') ?? '').trim(),
      lastName: (col(line, 'Last Name') ?? '').trim(),
      dateOfBirth: /^\d{4}-\d{2}-\d{2}$/.test(birthdate) ? birthdate : null,
      emails: [...new Set(emailColumns.flatMap((c) => splitValues(col(line, c))))],
      mobiles: [...new Set(phoneColumns.flatMap((c) => splitValues(col(line, c))))],
    };
  });
  return { rows };
}
