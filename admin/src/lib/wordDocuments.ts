import {
  AlignmentType,
  BorderStyle,
  Document,
  ExternalHyperlink,
  HeadingLevel,
  ImageRun,
  LevelFormat,
  Packer,
  Paragraph,
  Table,
  TableCell,
  TableRow,
  TextRun,
  WidthType,
  type ParagraphChild,
} from 'docx';

/**
 * Opens a .docx in the browser as HTML the editor understands (mammoth.js). Pictures come along embedded. Returns
 * notes about anything Word-specific that couldn't be kept (e.g. text boxes), so the editor can say so.
 */
export async function wordToHtml(file: ArrayBuffer): Promise<{ html: string; notes: string[] }> {
  const mammoth = await import('mammoth');
  // At runtime mammoth has 	ransforms, which its type definitions leave out.
  const { transforms } = mammoth as unknown as { transforms: { paragraph: (f: (p: WordParagraph) => WordParagraph) => (doc: unknown) => unknown } };
  const result = await mammoth.convertToHtml(
    { arrayBuffer: file },
    {
      styleMap: ['u => u', 'strike => s', ...ALIGNED_STYLES, "p[style-name='Title'] => h1:fresh", "p[style-name='Subtitle'] => h2:fresh", "p[style-name='Quote'] => blockquote > p:fresh"],
      // mammoth drops paragraph alignment; carry it through the style name and turn it back into text-align below.
      transformDocument: transforms.paragraph((p) => {
        const align = ({ center: 'center', right: 'right', both: 'justify', justify: 'justify' } as Record<string, string>)[p.alignment ?? ''];
        if (!align) return p;
        const base = ALIGNABLE.find((s) => s === p.styleName) ?? 'Normal';
        return { ...p, styleName: `${base}@@${align}` };
      }),
    },
  );
  const html = result.value.replace(/ class="align-(center|right|justify)"/g, ' style="text-align: $1"');
  const notes = [...new Set(result.messages.filter((m) => m.type === 'warning').map((m) => m.message.replace(/\s+/g, ' ').slice(0, 160)))];
  return { html, notes };
}

/** The parts of mammoth's paragraph we read. */
type WordParagraph = { alignment?: string | null; styleName?: string | null };

/** Word styles whose alignment is kept when opening, and what each becomes. */
const ALIGNABLE = ['Normal', 'Heading 1', 'Heading 2', 'Heading 3', 'Heading 4', 'Title', 'Subtitle'] as const;
const ALIGNED_STYLES = ALIGNABLE.flatMap((style) =>
  ['center', 'right', 'justify'].map((align) => {
    const tag = { Normal: 'p', 'Heading 1': 'h1', 'Heading 2': 'h2', 'Heading 3': 'h3', 'Heading 4': 'h4', Title: 'h1', Subtitle: 'h2' }[style];
    return `p[style-name='${style}@@${align}'] => ${tag}.align-${align}:fresh`;
  }),
);

type Marks = { bold?: boolean; italics?: boolean; underline?: boolean; strike?: boolean };
type Picture = { data: Uint8Array; type: 'png' | 'jpg' | 'gif' | 'bmp'; width: number; height: number };

const HEADINGS = { H1: HeadingLevel.HEADING_1, H2: HeadingLevel.HEADING_2, H3: HeadingLevel.HEADING_3, H4: HeadingLevel.HEADING_4 } as const;
const ALIGN = { left: AlignmentType.LEFT, center: AlignmentType.CENTER, right: AlignmentType.RIGHT, justify: AlignmentType.JUSTIFIED } as const;
/** Pictures are at most this wide in the saved file (about the width of an A4 page inside its margins), in pixels. */
const MAX_PICTURE_WIDTH = 600;

/** Saves the editor's HTML as a real .docx (docx library): headings, emphasis, links, lists, tables, pictures, alignment. */
export async function htmlToWord(html: string): Promise<Blob> {
  const body = new DOMParser().parseFromString(html, 'text/html').body;
  const pictures = await loadPictures(body);
  const children = blocks(body, pictures);
  const doc = new Document({
    creator: 'Shapers Church',
    styles: { default: { document: { run: { font: 'Calibri', size: 22 } } } },
    numbering: {
      config: [
        {
          reference: 'bullets',
          levels: [0, 1, 2, 3, 4].map((level) => ({
            level,
            format: LevelFormat.BULLET,
            text: ['•', '◦', '▪', '•', '◦'][level],
            alignment: AlignmentType.LEFT,
            style: { paragraph: { indent: { left: 720 * (level + 1), hanging: 360 } } },
          })),
        },
        {
          reference: 'numbers',
          levels: [0, 1, 2, 3, 4].map((level) => ({
            level,
            format: [LevelFormat.DECIMAL, LevelFormat.LOWER_LETTER, LevelFormat.LOWER_ROMAN, LevelFormat.DECIMAL, LevelFormat.LOWER_LETTER][level],
            text: `%${level + 1}.`,
            alignment: AlignmentType.LEFT,
            style: { paragraph: { indent: { left: 720 * (level + 1), hanging: 360 } } },
          })),
        },
      ],
    },
    sections: [{ children: children.length ? children : [new Paragraph('')] }],
  });
  return Packer.toBlob(doc);
}

type Block = Paragraph | Table;

function blocks(parent: Element, pictures: Map<string, Picture>, quote = false): Block[] {
  const out: Block[] = [];
  let loose: ParagraphChild[] = [];
  const flush = () => {
    if (loose.length) out.push(new Paragraph({ children: loose }));
    loose = [];
  };

  for (const node of Array.from(parent.childNodes)) {
    if (!(node instanceof Element)) {
      // Whitespace between blocks is layout, not text.
      if ((node.textContent ?? '').trim()) loose.push(...inline(node, {}, pictures));
      continue;
    }
    const tag = node.tagName;
    if (tag === 'P' || tag in HEADINGS) {
      flush();
      out.push(
        new Paragraph({
          children: inline(node, quote ? { italics: true } : {}, pictures),
          heading: tag in HEADINGS ? HEADINGS[tag as keyof typeof HEADINGS] : undefined,
          alignment: alignmentOf(node),
          indent: quote ? { left: 720 } : undefined,
        }),
      );
    } else if (tag === 'UL' || tag === 'OL') {
      flush();
      out.push(...list(node, tag === 'OL' ? 'numbers' : 'bullets', 0, pictures));
    } else if (tag === 'BLOCKQUOTE') {
      flush();
      out.push(...blocks(node, pictures, true));
    } else if (tag === 'HR') {
      flush();
      out.push(new Paragraph({ border: { bottom: { style: BorderStyle.SINGLE, size: 6, color: 'auto', space: 1 } } }));
    } else if (tag === 'TABLE') {
      flush();
      out.push(table(node, pictures));
    } else if (tag === 'DIV' || tag === 'SECTION' || tag === 'ARTICLE') {
      flush();
      out.push(...blocks(node, pictures, quote));
    } else {
      loose.push(...inline(node, {}, pictures));
    }
  }
  flush();
  return out;
}

function list(el: Element, reference: 'bullets' | 'numbers', level: number, pictures: Map<string, Picture>): Paragraph[] {
  const out: Paragraph[] = [];
  for (const li of Array.from(el.children).filter((c) => c.tagName === 'LI')) {
    const runs: ParagraphChild[] = [];
    const nested: Paragraph[] = [];
    for (const child of Array.from(li.childNodes)) {
      if (child instanceof Element && (child.tagName === 'UL' || child.tagName === 'OL')) {
        nested.push(...list(child, child.tagName === 'OL' ? 'numbers' : 'bullets', Math.min(level + 1, 4), pictures));
      } else if (child instanceof Element && child.tagName === 'P') {
        if (runs.length) runs.push(new TextRun({ break: 1 }));
        runs.push(...inline(child, {}, pictures));
      } else {
        runs.push(...inline(child, {}, pictures));
      }
    }
    out.push(new Paragraph({ children: runs, numbering: { reference, level } }), ...nested);
  }
  return out;
}

function table(el: Element, pictures: Map<string, Picture>): Table {
  const rows = Array.from(el.querySelectorAll('tr')).filter((tr) => tr.closest('table') === el);
  const columns = Math.max(1, ...rows.map((tr) => Array.from(tr.children).reduce((n, c) => n + (Number(c.getAttribute('colspan')) || 1), 0)));
  return new Table({
    width: { size: 100, type: WidthType.PERCENTAGE },
    rows: rows.map(
      (tr) =>
        new TableRow({
          // Only set for header rows: an explicit false still marks the row as a header for some readers.
          ...(Array.from(tr.children).every((c) => c.tagName === 'TH') ? { tableHeader: true } : {}),
          children: Array.from(tr.children)
            .filter((c) => c.tagName === 'TD' || c.tagName === 'TH')
            .map((cell) => {
              const content = blocks(cell, pictures);
              return new TableCell({
                columnSpan: Number(cell.getAttribute('colspan')) || undefined,
                rowSpan: Number(cell.getAttribute('rowspan')) || undefined,
                width: { size: Math.floor(100 / columns), type: WidthType.PERCENTAGE },
                // A cell must hold at least one paragraph; header cells are bold.
                children: content.length
                  ? cell.tagName === 'TH'
                    ? blocks(boldClone(cell), pictures)
                    : content
                  : [new Paragraph('')],
              });
            }),
        }),
    ),
  });
}

function inline(node: Node, marks: Marks, pictures: Map<string, Picture>): ParagraphChild[] {
  if (node.nodeType === Node.TEXT_NODE) {
    const text = (node.textContent ?? '').replace(/\s+/g, ' ');
    return text ? [run(text, marks)] : [];
  }
  if (!(node instanceof Element)) return [];
  const tag = node.tagName;
  if (tag === 'BR') return [new TextRun({ break: 1 })];
  if (tag === 'IMG') {
    const picture = pictures.get(node.getAttribute('src') ?? '');
    return picture ? [new ImageRun({ type: picture.type, data: picture.data, transformation: { width: picture.width, height: picture.height } })] : [];
  }
  const next: Marks = {
    ...marks,
    bold: marks.bold || tag === 'STRONG' || tag === 'B' || tag === 'TH',
    italics: marks.italics || tag === 'EM' || tag === 'I',
    underline: marks.underline || tag === 'U',
    strike: marks.strike || tag === 'S' || tag === 'DEL' || tag === 'STRIKE',
  };
  const children = Array.from(node.childNodes).flatMap((c) => inline(c, next, pictures));
  if (tag === 'A') {
    const href = node.getAttribute('href') ?? '';
    if (/^(https?:|mailto:)/i.test(href)) {
      const linked = Array.from(node.childNodes).flatMap((c) => inline(c, { ...next, underline: true }, pictures));
      return [new ExternalHyperlink({ link: href, children: linked.length ? linked : [run(href, next)] })];
    }
  }
  return children;
}

function run(text: string, marks: Marks) {
  return new TextRun({ text, bold: marks.bold, italics: marks.italics, strike: marks.strike, underline: marks.underline ? {} : undefined });
}

function alignmentOf(el: Element) {
  const value = (el as HTMLElement).style?.textAlign as keyof typeof ALIGN | undefined;
  return value && value in ALIGN ? ALIGN[value] : undefined;
}

function boldClone(cell: Element) {
  const clone = cell.cloneNode(true) as Element;
  const wrapper = cell.ownerDocument.createElement('strong');
  wrapper.append(...Array.from(clone.childNodes));
  const p = cell.ownerDocument.createElement('p');
  p.append(wrapper);
  const holder = cell.ownerDocument.createElement('div');
  holder.append(p);
  return holder;
}

/** Reads every embedded picture once: its bytes, format and a size that fits the page. */
async function loadPictures(body: HTMLElement): Promise<Map<string, Picture>> {
  const pictures = new Map<string, Picture>();
  const sources = [...new Set(Array.from(body.querySelectorAll('img')).map((img) => img.getAttribute('src') ?? ''))].filter(Boolean);
  await Promise.all(
    sources.map(async (src) => {
      try {
        let blob = await (await fetch(src)).blob();
        const bitmap = await createImageBitmap(blob);
        const kinds: Record<string, Picture['type']> = { 'image/png': 'png', 'image/jpeg': 'jpg', 'image/gif': 'gif', 'image/bmp': 'bmp' };
        let type = kinds[blob.type];
        if (!type) {
          // Word can't hold WebP and the like: redraw it as PNG.
          const canvas = new OffscreenCanvas(bitmap.width, bitmap.height);
          canvas.getContext('2d')?.drawImage(bitmap, 0, 0);
          blob = await canvas.convertToBlob({ type: 'image/png' });
          type = 'png';
        }
        const scale = Math.min(1, MAX_PICTURE_WIDTH / bitmap.width);
        pictures.set(src, { data: new Uint8Array(await blob.arrayBuffer()), type, width: Math.round(bitmap.width * scale), height: Math.round(bitmap.height * scale) });
        bitmap.close();
      } catch {
        // A picture that can't be read is left out rather than stopping the save.
      }
    }),
  );
  return pictures;
}
