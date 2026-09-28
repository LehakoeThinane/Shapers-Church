import { useMemo, useState, type ReactNode } from 'react';
import { covers, useAccess } from './access';
import { useScopes } from './queries';
import { ScopeContext, type ScopeValue } from './scope-context';

/** Offers only the parts of the church the signed-in user holds some permission in. */
export function ScopeProvider({ children }: { children: ReactNode }) {
  const [current, setCurrent] = useState<string | null>(null);
  const { data: access } = useAccess();
  const { data: scopes = [] } = useScopes();

  const value = useMemo<ScopeValue>(() => {
    const granted = new Set(access?.permissions.flatMap((p) => p.scopes) ?? []);
    const options = scopes.filter((s) => [...granted].some((g) => covers(g, s.path)));
    const names = new Map(scopes.map((s) => [s.path, s.name]));
    return { current, setCurrent, options, nameOf: (scope) => names.get(scope) ?? scope };
  }, [access, scopes, current]);

  return <ScopeContext.Provider value={value}>{children}</ScopeContext.Provider>;
}
