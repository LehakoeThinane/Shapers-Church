import type { ButtonHTMLAttributes, InputHTMLAttributes, ReactNode, SelectHTMLAttributes } from 'react';
import { errorMessage } from '../lib/api';

export function Card({ title, actions, children, className = '' }: { title?: ReactNode; actions?: ReactNode; children: ReactNode; className?: string }) {
  return (
    <section className={`card glass ${className}`}>
      {(title || actions) && (
        <header className="card-header">
          {title && <h2>{title}</h2>}
          {actions && <div className="row">{actions}</div>}
        </header>
      )}
      {children}
    </section>
  );
}

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & { variant?: 'primary' | 'secondary' | 'danger' | 'ghost'; busy?: boolean };

export function Button({ variant = 'secondary', busy, children, disabled, className = '', ...rest }: ButtonProps) {
  return (
    <button className={`btn btn-${variant} ${className}`} disabled={disabled || busy} {...rest}>
      {busy ? 'Working…' : children}
    </button>
  );
}

export function Field({ label, hint, children }: { label: string; hint?: string; children: ReactNode }) {
  return (
    <label className="field">
      <span className="field-label">{label}</span>
      {children}
      {hint && <span className="field-hint">{hint}</span>}
    </label>
  );
}

export function TextInput(props: InputHTMLAttributes<HTMLInputElement>) {
  return <input className="input" {...props} />;
}

export function Select(props: SelectHTMLAttributes<HTMLSelectElement>) {
  return <select className="input" {...props} />;
}

export function Badge({ tone = 'neutral', children }: { tone?: 'neutral' | 'accent' | 'danger' | 'success'; children: ReactNode }) {
  return <span className={`badge badge-${tone}`}>{children}</span>;
}

/** A filter that is on or off. `dot` names a colour class (e.g. "kind-event") shown before the label as its key. */
export function ToggleChip({ on, onChange, dot, children }: { on: boolean; onChange: (on: boolean) => void; dot?: string; children: ReactNode }) {
  return (
    <button type="button" className={`chip${on ? ' chip-on' : ''}`} aria-pressed={on} onClick={() => onChange(!on)}>
      {dot && <span className={`chip-dot ${dot}`} aria-hidden="true" />}
      {children}
    </button>
  );
}

export function ErrorNote({ error }: { error: unknown }) {
  if (!error) return null;
  return (
    <p className="note note-danger" role="alert">
      {errorMessage(error)}
    </p>
  );
}

export function Empty({ children }: { children: ReactNode }) {
  return <p className="empty">{children}</p>;
}

export function Loading() {
  return <p className="empty">Loading…</p>;
}

export function PageHeader({ title, subtitle, actions }: { title: string; subtitle?: ReactNode; actions?: ReactNode }) {
  return (
    <div className="page-header">
      <div>
        <h1>{title}</h1>
        {subtitle && <p className="muted">{subtitle}</p>}
      </div>
      {actions && <div className="row">{actions}</div>}
    </div>
  );
}
