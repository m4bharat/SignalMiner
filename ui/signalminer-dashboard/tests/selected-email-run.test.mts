import test from 'node:test';
import assert from 'node:assert/strict';
import { hasUnresolvedVariables, runReviewedEmails } from '../src/app/selected-email-run.ts';

test('individual reviewed payloads are sent once; no delay after final message', async () => {
  const sent: string[] = []; const waits: number[] = [];
  const rows = [{ name: 'Alice', subject: 'Hello Alice' }, { name: 'Bob', subject: 'Hello Bob' }];
  const result = await runReviewedEmails(rows, async row => { sent.push(row.subject); }, row => row.name, 10,
    new AbortController().signal, () => {}, async ms => { waits.push(ms); });
  assert.deepEqual(sent, ['Hello Alice', 'Hello Bob']); assert.deepEqual(waits, [10000]);
  assert.equal(result.sent, 2); assert.equal(result.remaining, 0);
});
test('unresolved variables including attributes and multiline tokens are detected', () => {
  assert.equal(hasUnresolvedVariables('Hi {{firstName}}'), true);
  assert.equal(hasUnresolvedVariables('<a href="{{url}}">Hello</a>'), true);
  assert.equal(hasUnresolvedVariables('{{\nmissing-value\n}}'), true);
  assert.equal(hasUnresolvedVariables('Hi Alice'), false);
});
test('stop during in-flight send prevents next message and delay', async () => {
  const stop = new AbortController(); let calls = 0; let waits = 0;
  const result = await runReviewedEmails([1,2,3], async () => { calls++; stop.abort(); }, String, 10,
    stop.signal, () => {}, async () => { waits++; });
  assert.equal(calls, 1); assert.equal(waits, 0); assert.equal(result.sent, 1); assert.equal(result.stopped, 2);
});
test('stop during delay prevents next message', async () => {
  const stop = new AbortController(); let calls = 0;
  const result = await runReviewedEmails([1,2], async () => { calls++; }, String, 10,
    stop.signal, () => {}, async () => { stop.abort(); });
  assert.equal(calls, 1); assert.equal(result.stopped, 1);
});
test('partial failures and suppression have accurate counts without retries', async () => {
  const calls: number[] = [];
  const result = await runReviewedEmails([1,2,3,4], async item => { calls.push(item); if(item===2) throw {status:400}; if(item===3) throw {status:409}; }, String, 10,
    new AbortController().signal, () => {}, async () => {});
  assert.deepEqual(calls,[1,2,3,4]); assert.equal(result.sent,2); assert.equal(result.failed,1); assert.equal(result.skipped,1);
});
test('limit and uncertain network responses stop remaining requests', async () => {
  for (const status of [429,0,502]) {
    const result = await runReviewedEmails([1,2], async () => { throw {status}; }, String, 10,
      new AbortController().signal, () => {}, async () => { throw new Error('must not wait'); });
    assert.equal(result.failed,1); assert.equal(result.stopped,1);
  }
});
