import { useEffect, useRef, useState, useSyncExternalStore, type FormEvent } from 'react';
import { currentDialog, publishDialog, subscribeDialog, type DialogRequest } from '../lib/dialogs';

/** Shows the current dialog. Mounted once, at the top of the portal. */
export function DialogHost() {
  const request = useSyncExternalStore(subscribeDialog, currentDialog);

  return request ? <DialogView key={request.title + (request.message ?? '')} request={request} /> : null;
}

function DialogView({ request }: { request: DialogRequest }) {
  const ref = useRef<HTMLDialogElement>(null);
  const [value, setValue] = useState(request.field?.value ?? '');

  const finish = (answer: string | boolean | null) => {
    publishDialog(null);
    request.resolve(answer);
  };

  useEffect(() => {
    const dialog = ref.current;
    if (dialog && !dialog.open) dialog.showModal();
  }, []);

  const submit = (e: FormEvent) => {
    e.preventDefault();
    finish(request.field ? value : true);
  };

  return (
    <dialog
      ref={ref}
      className="dialog glass"
      aria-labelledby="dialog-title"
      onCancel={(e) => {
        e.preventDefault();
        finish(request.field ? null : false);
      }}
      onClick={(e) => {
        // A click on the dimmed backdrop (outside the box) cancels.
        if (e.target === ref.current) finish(request.field ? null : false);
      }}>
      <form className="dialog-body" onSubmit={submit}>
        <h2 id="dialog-title">{request.title}</h2>
        {request.message && <p className="muted">{request.message}</p>}
        {request.field && (
          <label className="field">
            <span className="field-label">{request.field.label}</span>
            <input className="input" autoFocus value={value} placeholder={request.field.placeholder} onChange={(e) => setValue(e.target.value)} />
          </label>
        )}
        <div className="dialog-actions">
          {/* For something that can't be undone, Cancel has the focus, so Enter doesn't do it by accident. */}
          <button type="button" className="btn btn-ghost" autoFocus={request.danger && !request.field} onClick={() => finish(request.field ? null : false)}>
            {request.cancelLabel}
          </button>
          <button type="submit" className={`btn ${request.danger ? 'btn-danger' : 'btn-primary'}`} autoFocus={!request.field && !request.danger}>
            {request.confirmLabel}
          </button>
        </div>
      </form>
    </dialog>
  );
}
