/** What a dialog asks. A `field` turns it into a question with a typed answer. */
export type DialogRequest = {
  title: string;
  message?: string;
  confirmLabel: string;
  cancelLabel: string;
  danger: boolean;
  field?: { label: string; value: string; placeholder?: string };
  resolve: (answer: string | boolean | null) => void;
};

// One dialog at a time, shared by the whole portal: askConfirm/askText put it up, <DialogHost /> shows it.
let current: DialogRequest | null = null;
const listeners = new Set<() => void>();
export const publishDialog = (next: DialogRequest | null) => {
  current = next;
  listeners.forEach((l) => l());
};

/**
 * Asks to confirm something, in the portal's own centred dialog (in place of the browser's confirm box).
 * Resolves true for the confirm button, false for Cancel, Escape or a click outside.
 */
export function askConfirm(
  message: string,
  options: { title?: string; confirmLabel?: string; cancelLabel?: string; danger?: boolean } = {},
): Promise<boolean> {
  const destructive = options.danger ?? /\b(delete|remove|cancel|ban|erase|archive)\b/i.test(message);
  return new Promise((resolve) => {
    current?.resolve(current.field ? null : false);
    publishDialog({
      title: options.title ?? (destructive ? 'Are you sure?' : 'Please confirm'),
      message,
      confirmLabel: options.confirmLabel ?? (destructive ? 'Yes, go ahead' : 'OK'),
      cancelLabel: options.cancelLabel ?? 'Cancel',
      danger: destructive,
      resolve: (answer) => resolve(answer === true),
    });
  });
}

/** Asks for a short piece of text (in place of the browser's prompt box). Resolves null when cancelled. */
export function askText(
  title: string,
  options: { label?: string; value?: string; placeholder?: string; message?: string; confirmLabel?: string } = {},
): Promise<string | null> {
  return new Promise((resolve) => {
    current?.resolve(current.field ? null : false);
    publishDialog({
      title,
      message: options.message,
      confirmLabel: options.confirmLabel ?? 'OK',
      cancelLabel: 'Cancel',
      danger: false,
      field: { label: options.label ?? title, value: options.value ?? '', placeholder: options.placeholder },
      resolve: (answer) => resolve(typeof answer === 'string' ? answer : null),
    });
  });
}

/** For <DialogHost />: follow the current dialog. */
export const subscribeDialog = (listener: () => void) => {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
};

export const currentDialog = () => current;