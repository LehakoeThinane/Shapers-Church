import { useMutation } from '@tanstack/react-query';
import { useState } from 'react';
import { api, unwrap, type Schemas } from '../lib/api';
import { useCanDraft, useReviewDraft, type Draft } from '../lib/assist';
import { Badge, Button, ErrorNote } from './ui';

/** Marks AI-written text until a person has made it their own. */
export function AiBadge() {
  return (
    <Badge tone="accent">
      <span title="Written by AI. Check it carefully and change anything that isn't right before using it.">AI draft</span>
    </Badge>
  );
}

const modes: { mode: Schemas['RewriteMode']; label: string }[] = [
  { mode: 'Tidy', label: 'Tidy up' },
  { mode: 'Shorter', label: 'Shorter' },
  { mode: 'Longer', label: 'Longer' },
];

/**
 * "Tidy up / Shorter / Longer" under a text box. The suggestion appears below; nothing changes until the person
 * chooses to use it, and they can still edit it afterwards.
 */
export function RewriteHelp({ text, onApply }: { text: string; onApply: (text: string) => void }) {
  const canDraft = useCanDraft();
  const review = useReviewDraft();
  const [draft, setDraft] = useState<Draft | null>(null);
  const rewrite = useMutation({
    mutationFn: async (mode: Schemas['RewriteMode']) => unwrap(await api.POST('/api/admin/assist/drafts/rewrite', { body: { text, mode } })),
    onSuccess: setDraft,
  });

  if (!canDraft) return null;
  return (
    <div className="ai-help stack-tight">
      <div className="row">
        <span className="small muted">Writing help</span>
        {modes.map((m) => (
          <button key={m.mode} type="button" className="link-button" disabled={!text.trim() || rewrite.isPending} onClick={() => rewrite.mutate(m.mode)}>
            {m.label}
          </button>
        ))}
        {rewrite.isPending && <span className="small muted">Writing…</span>}
      </div>
      <ErrorNote error={rewrite.error} />
      {draft?.rewrite && (
        <div className="ai-suggestion stack-tight">
          <span>
            <AiBadge />
          </span>
          <p className="pre-wrap">{draft.rewrite.text}</p>
          <div className="row">
            <Button
              onClick={() => {
                onApply(draft.rewrite!.text);
                review.mutate({ id: draft.id, action: 'accept' });
                setDraft(null);
              }}
            >
              Use this
            </Button>
            <Button
              variant="ghost"
              onClick={() => {
                review.mutate({ id: draft.id, action: 'discard' });
                setDraft(null);
              }}
            >
              Discard
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}
