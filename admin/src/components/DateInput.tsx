import { useEffect, useId, useRef, useState } from 'react';

/** A date as the API sends it: "2026-10-25". */
type IsoDate = string;

const monthNames = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
const weekdays = ['Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa', 'Su'];

const pad = (n: number) => String(n).padStart(2, '0');
const toIso = (y: number, m: number, d: number): IsoDate => `${y}-${pad(m + 1)}-${pad(d)}`;
const parse = (iso: string | undefined | null) => {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso ?? '');
  return match ? { y: Number(match[1]), m: Number(match[2]) - 1, d: Number(match[3]) } : null;
};
const todayIso = () => {
  const now = new Date();
  return toIso(now.getFullYear(), now.getMonth(), now.getDate());
};

/** "Sun 25 Oct 2026". */
function label(iso: IsoDate) {
  const p = parse(iso);
  if (!p) return '';
  return new Intl.DateTimeFormat('en-ZA', { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric' }).format(new Date(p.y, p.m, p.d));
}

/**
 * The portal's date picker, in place of the browser's: month and year jump straight from the header (no scrolling
 * through decades for a date of birth), weeks start on Monday, and it matches the portal's look. Values are
 * "yyyy-mm-dd" strings, like a native date input's.
 */
export function DateInput({
  value,
  onChange,
  min,
  max,
  required,
  disabled,
  placeholder = 'Choose a date',
  'aria-label': ariaLabel,
}: {
  value: IsoDate | null | undefined;
  onChange: (value: IsoDate) => void;
  min?: IsoDate;
  max?: IsoDate;
  required?: boolean;
  disabled?: boolean;
  placeholder?: string;
  'aria-label'?: string;
}) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const dialogId = useId();
  const [today] = useState(todayIso);
  const selected = parse(value);
  const start = selected ?? parse(max && max < today ? max : min && min > today ? min : today)!;
  const [view, setView] = useState({ y: start.y, m: start.m });

  useEffect(() => {
    if (!open) return;
    const close = (e: MouseEvent) => !ref.current?.contains(e.target as Node) && setOpen(false);
    const escape = (e: KeyboardEvent) => e.key === 'Escape' && setOpen(false);
    document.addEventListener('mousedown', close);
    document.addEventListener('keydown', escape);
    return () => {
      document.removeEventListener('mousedown', close);
      document.removeEventListener('keydown', escape);
    };
  }, [open]);

  const toggle = () => {
    if (!open) {
      const at = parse(value) ?? start;
      setView({ y: at.y, m: at.m });
    }
    setOpen(!open);
  };
  const choose = (iso: IsoDate) => {
    onChange(iso);
    setOpen(false);
  };
  const shift = (months: number) =>
    setView((v) => {
      const total = v.y * 12 + v.m + months;
      return { y: Math.floor(total / 12), m: total % 12 };
    });
  const allowed = (iso: IsoDate) => (!min || iso >= min) && (!max || iso <= max);

  // Years to jump between: the allowed range, or from 1920 to ten years ahead.
  const firstYear = parse(min)?.y ?? Math.min(1920, view.y);
  const lastYear = parse(max)?.y ?? Math.max(parse(today)!.y + 10, view.y);
  const years = Array.from({ length: lastYear - firstYear + 1 }, (_, i) => lastYear - i);

  // Six weeks from the Monday on or before the 1st.
  const lead = (new Date(view.y, view.m, 1).getDay() + 6) % 7;
  const days = Array.from({ length: 42 }, (_, i) => {
    const date = new Date(view.y, view.m, 1 - lead + i);
    return { iso: toIso(date.getFullYear(), date.getMonth(), date.getDate()), day: date.getDate(), inMonth: date.getMonth() === view.m };
  });

  return (
    <div className="date-input" ref={ref}>
      <button
        type="button"
        className="input date-input-button"
        disabled={disabled}
        aria-label={ariaLabel ? `${ariaLabel}: ${value ? label(value) : 'not set'}` : undefined}
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-controls={open ? dialogId : undefined}
        onClick={toggle}>
        <span className={value ? undefined : 'date-input-placeholder'}>{value ? label(value) : placeholder}</span>
        <svg viewBox="0 0 24 24" aria-hidden="true" className="date-input-icon">
          <rect x="3.5" y="5" width="17" height="15.5" rx="3" />
          <path d="M3.5 10h17M8 3v4M16 3v4" />
        </svg>
      </button>
      {/* Keeps the browser's "please fill in this field" check on required dates. */}
      {required && <input className="date-input-required" tabIndex={-1} aria-hidden="true" required value={value ?? ''} onChange={() => undefined} />}
      {open && (
        <div
          className="date-popover glass"
          role="dialog"
          id={dialogId}
          aria-label={ariaLabel ?? 'Choose a date'}
          // Date fields sit inside a <label>: a click on empty space would otherwise "click" the button and close it.
          onClick={(e) => !(e.target as HTMLElement).closest('button, select') && e.preventDefault()}>
          <div className="date-popover-head">
            <button type="button" className="date-nav" aria-label="Previous month" onClick={() => shift(-1)}>
              ‹
            </button>
            <select className="date-select" aria-label="Month" value={view.m} onChange={(e) => setView({ ...view, m: Number(e.target.value) })}>
              {monthNames.map((name, i) => (
                <option key={name} value={i}>
                  {name}
                </option>
              ))}
            </select>
            <select className="date-select" aria-label="Year" value={view.y} onChange={(e) => setView({ ...view, y: Number(e.target.value) })}>
              {years.map((y) => (
                <option key={y} value={y}>
                  {y}
                </option>
              ))}
            </select>
            <button type="button" className="date-nav" aria-label="Next month" onClick={() => shift(1)}>
              ›
            </button>
          </div>
          <div className="date-grid" role="grid">
            {weekdays.map((w) => (
              <span key={w} className="date-weekday" role="columnheader">
                {w}
              </span>
            ))}
            {days.map((d) => (
              <button
                key={d.iso}
                type="button"
                role="gridcell"
                className={['date-day', d.inMonth ? '' : 'date-day-outside', d.iso === today ? 'date-day-today' : '', d.iso === value ? 'date-day-selected' : ''].join(' ')}
                disabled={!allowed(d.iso)}
                aria-selected={d.iso === value}
                aria-label={label(d.iso)}
                onClick={() => choose(d.iso)}>
                {d.day}
              </button>
            ))}
          </div>
          <div className="date-popover-foot">
            <button type="button" className="date-link" disabled={!allowed(today)} onClick={() => choose(today)}>
              Today
            </button>
            {!required && value && (
              <button type="button" className="date-link" onClick={() => choose('')}>
                Clear
              </button>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
