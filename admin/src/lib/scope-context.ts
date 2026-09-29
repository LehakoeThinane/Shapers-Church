import { createContext, useContext } from 'react';
import type { Schemas } from './api';

export type ScopeOption = Schemas['ScopeSummary'];

export interface ScopeValue {
  /** The scope staff are working in. Null means "everything I can see". */
  current: string | null;
  setCurrent: (scope: string | null) => void;
  options: ScopeOption[];
  nameOf: (scope: string) => string;
}

export const ScopeContext = createContext<ScopeValue | null>(null);

export function useScope(): ScopeValue {
  const value = useContext(ScopeContext);
  if (!value) throw new Error('useScope must be used inside ScopeProvider');
  return value;
}

/** Indents a scope by depth for use in a <select>. */
export function scopeLabel(option: ScopeOption): string {
  const depth = option.path.split('.').length - 1;
  return `${' '.repeat(depth)}${option.name}`;
}
