export interface SendProgress {
  sent: number; failed: number; skipped: number; stopped: number; remaining: number; current: string;
}

export function hasUnresolvedVariables(value: string): boolean { return /\{\{[\s\S]*?\}\}/.test(value); }

export function waitForNext(milliseconds: number, signal: AbortSignal): Promise<void> {
  return new Promise(resolve => {
    if (signal.aborted) { resolve(); return; }
    const finish = () => { clearTimeout(timer); signal.removeEventListener('abort', finish); resolve(); };
    const timer = setTimeout(finish, milliseconds);
    signal.addEventListener('abort', finish, { once: true });
  });
}

export async function runReviewedEmails<T>(items: readonly T[], send: (item: T) => Promise<void>,
  label: (item: T) => string, delaySeconds: number, signal: AbortSignal,
  changed: (state: SendProgress) => void, wait = waitForNext): Promise<SendProgress> {
  const state: SendProgress = { sent: 0, failed: 0, skipped: 0, stopped: 0, remaining: items.length, current: '' };
  let halt = false;
  for (let index = 0; index < items.length; index++) {
    if (signal.aborted || halt) { state.stopped = items.length - index; break; }
    state.current = label(items[index]); changed({ ...state });
    try { await send(items[index]); state.sent++; }
    catch (error) {
      const status = (error as { status?: number }).status;
      if (status === 409) state.skipped++; else state.failed++;
      // A network failure can hide a successful SMTP submission. Do not continue or retry.
      halt = status === 429 || status === 0 || status === undefined || (status !== undefined && status >= 500);
    }
    state.remaining = items.length - index - 1; changed({ ...state });
    if (index < items.length - 1 && !signal.aborted && !halt) await wait(delaySeconds * 1000, signal);
  }
  state.remaining = 0; state.current = ''; changed({ ...state });
  return state;
}
