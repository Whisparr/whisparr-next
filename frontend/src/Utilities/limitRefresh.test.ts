import assert from 'node:assert/strict';
import { test } from 'node:test';
import limitRefresh from './limitRefresh.ts';

const sleep = (ms: number) =>
  new Promise((resolve) => {
    setTimeout(resolve, ms);
  });

function countingRefresh(durationMs: number) {
  const counts = { started: 0, running: 0, peak: 0 };

  const refresh = async () => {
    counts.started++;
    counts.running++;
    counts.peak = Math.max(counts.peak, counts.running);
    await sleep(durationMs);
    counts.running--;
  };

  return { counts, refresh };
}

test('a burst runs one refresh at a time plus one trailing refresh', async () => {
  const { counts, refresh } = countingRefresh(20);
  const limiter = limitRefresh(refresh, 50);

  for (let i = 0; i < 100; i++) {
    limiter.request();
  }

  await sleep(200);

  assert.equal(counts.started, 2);
  assert.equal(counts.peak, 1);
});

test('the gap starts after the refresh finishes', async () => {
  const { counts, refresh } = countingRefresh(100);
  const limiter = limitRefresh(refresh, 100);

  limiter.request();
  await sleep(20);
  limiter.request();

  // A gap alongside the refresh would have ended by now and started the trailing one
  await sleep(150);
  assert.equal(counts.started, 1);

  await sleep(150);
  assert.equal(counts.started, 2);
});

test('flush skips the gap', async () => {
  const { counts, refresh } = countingRefresh(10);
  const limiter = limitRefresh(refresh, 1000);

  limiter.request();
  await sleep(30);
  limiter.request();
  limiter.flush();
  await sleep(30);

  assert.equal(counts.started, 2);

  limiter.flush();
});

test('flush with nothing running does not refresh', async () => {
  const { counts, refresh } = countingRefresh(10);
  const limiter = limitRefresh(refresh, 10);

  limiter.flush();
  await sleep(30);

  assert.equal(counts.started, 0);
});

test('a failed refresh does not block later ones', async () => {
  let attempts = 0;
  const limiter = limitRefresh(() => {
    attempts++;

    return attempts === 1 ? Promise.reject(new Error('boom')) : sleep(1);
  }, 10);

  await assert.rejects(limiter.request());
  await limiter.request();

  assert.equal(attempts, 2);
});
