import { Marked } from 'marked';

const zone = 'Africa/Johannesburg';

export const longDate = (iso: string) => new Intl.DateTimeFormat('en-ZA', { day: 'numeric', month: 'long', year: 'numeric', timeZone: zone }).format(new Date(iso));

export const dayAndTime = (iso: string) =>
  new Intl.DateTimeFormat('en-ZA', { weekday: 'long', day: 'numeric', month: 'long', hour: '2-digit', minute: '2-digit', timeZone: zone }).format(new Date(iso));

export const time = (iso: string) => new Intl.DateTimeFormat('en-ZA', { hour: '2-digit', minute: '2-digit', timeZone: zone }).format(new Date(iso));

export const minutes = (seconds: number | null | undefined) => (seconds ? `${Math.max(1, Math.round(seconds / 60))} min` : null);

const escape = (value: string) => value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

/**
 * Markdown to HTML for staff-written content. Raw HTML in the source is shown as text, never run, and links
 * may only be web or email addresses, so a compromised editor account can't inject scripts into the site.
 */
const markdown = new Marked({
  gfm: true,
  renderer: {
    html: ({ text }) => escape(text),
    link({ href, title, tokens }) {
      const safe = /^(https?:|mailto:|tel:|\/)/i.test(href) ? href : '#';
      const external = /^https?:/i.test(safe) && !safe.includes('shaperschurch.com');
      const label = this.parser.parseInline(tokens);
      return `<a href="${escape(safe)}"${title ? ` title="${escape(title)}"` : ''}${external ? ' rel="noopener" target="_blank"' : ''}>${label}</a>`;
    },
    image: ({ href, text: alt }) => (/^https:/i.test(href) ? `<img src="${escape(href)}" alt="${escape(alt)}" loading="lazy">` : ''),
  },
});

export const renderMarkdown = (source: string | null | undefined) => (source ? (markdown.parse(source, { async: false }) as string) : '');
