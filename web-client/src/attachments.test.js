import assert from "node:assert/strict";
import test from "node:test";
import { attachmentsApi } from "./api/attachments.js";
import { ApiError } from "./api/client.js";

const id = "11111111-2222-4333-8444-555555555555";

test("bounded streaming downloads cancel on overlimit data", async () => {
  const oldFetch = globalThis.fetch;
  let reads = 0, cancelled = false, released = false;
  globalThis.fetch = async (_path, options) => {
    assert.equal(options.cache, "no-store");
    return {
      ok: true, status: 200,
      headers: { get: () => null },
      body: { getReader: () => ({
        async read() {
          reads++;
          // Fake chunks report sizes without allocating a huge buffer.
          return { done: false, value: { byteLength: reads === 1 ? 10485760 : 1 } };
        },
        async cancel() { cancelled = true; },
        releaseLock() { released = true; }
      }) }
    };
  };
  try {
    await assert.rejects(
      attachmentsApi.download("session", id),
      error => error instanceof RangeError && /exceeds/.test(error.message)
    );
    assert.equal(reads, 2);
    assert.equal(cancelled, true);
    assert.equal(released, true);
  } finally {
    globalThis.fetch = oldFetch;
  }
});

test("authorized binary transfer and denied scope preserve correct behavior", async () => {
  const oldFetch = globalThis.fetch;
  let reads = 0;
  globalThis.fetch = async () => ({
    ok: true, status: 200,
    headers: { get: () => null },
    body: { getReader: () => ({
      async read() {
        return ++reads === 1
          ? { done: false, value: new Uint8Array([7, 8, 9]) }
          : { done: true };
      },
      releaseLock() {}
    }) }
  });
  try {
    const file = await attachmentsApi.download("session", id);
    assert.deepEqual(new Uint8Array(await file.arrayBuffer()), new Uint8Array([7, 8, 9]));
    globalThis.fetch = async () => ({ ok: false, status: 403 });
    await assert.rejects(attachmentsApi.download("session", id),
      error => error instanceof ApiError && error.status === 403);
  } finally {
    globalThis.fetch = oldFetch;
  }
});
