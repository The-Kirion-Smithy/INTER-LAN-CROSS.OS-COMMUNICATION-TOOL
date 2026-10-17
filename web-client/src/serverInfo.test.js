import assert from "node:assert/strict";
import test from "node:test";
import { fetchServerInfo, normalizeServerInfo } from "./serverInfo.js";

const payload = (server = {}) => ({
  httpsPort: 7443,
  certificateSha256: "not-for-the-ui-model",
  server: {
    configured: true,
    serverName: "  Owner LAN  ",
    apiVersion: "v1",
    runtimeMode: 1,
    ...server,
  },
});

test("extracts only the UI-safe server fields", () => {
  assert.deepEqual(normalizeServerInfo(payload()), {
    configured: true,
    serverName: "Owner LAN",
    apiVersion: "v1",
    runtimeMode: "1",
  });
});

test("supports string runtime modes", () => {
  assert.equal(normalizeServerInfo(payload({ runtimeMode: "ServerOwner" })).runtimeMode, "ServerOwner");
});

test("does not invent a configured owner for an unconfigured server", () => {
  assert.equal(
    normalizeServerInfo(payload({ configured: false, serverName: null, runtimeMode: 0 })).serverName,
    "Server not configured",
  );
});

test("uses an explicit fallback for unnamed configured servers", () => {
  assert.equal(normalizeServerInfo(payload({ serverName: "  " })).serverName, "Unnamed server");
});

test("rejects missing and malformed response envelopes", () => {
  for (const invalid of [null, {}, { server: null }, { server: [] }, { server: "hi" }]) {
    assert.throws(() => normalizeServerInfo(invalid), /invalid status response/);
  }
});

test("rejects missing configuration and protocol metadata", () => {
  assert.throws(
    () => normalizeServerInfo(payload({ configured: "true" })),
    /configuration state/,
  );
  assert.throws(
    () => normalizeServerInfo(payload({ apiVersion: "" })),
    /API version/,
  );
  assert.throws(
    () => normalizeServerInfo(payload({ runtimeMode: null })),
    /runtime mode/,
  );
});

test("requests a fresh status with a cancellable fetch", async () => {
  let observed;
  const result = await fetchServerInfo({
    fetchImpl: async (url, options) => {
      observed = { url, options };
      return { ok: true, json: async () => payload() };
    },
  });
  assert.equal(observed.url, "/api/v1/server/info");
  assert.equal(observed.options.cache, "no-store");
  assert.equal(observed.options.signal.aborted, false);
  assert.equal(result.serverName, "Owner LAN");
});

test("fails clearly on non-2xx responses", async () => {
  await assert.rejects(
    fetchServerInfo({ fetchImpl: async () => ({ ok: false, status: 503 }) }),
    /HTTP 503/,
  );
});

test("rejects invalid timeout configuration", async () => {
  await assert.rejects(
    fetchServerInfo({ timeoutMs: 0, fetchImpl: async () => ({ ok: true }) }),
    RangeError,
  );
});

test("propagates caller cancellation to fetch", async () => {
  const caller = new AbortController();
  let observedSignal;
  const pending = fetchServerInfo({
    signal: caller.signal,
    fetchImpl: (_url, { signal }) => {
      observedSignal = signal;
      return new Promise((_resolve, reject) => {
        signal.addEventListener("abort", () => reject(new Error("cancelled")), { once: true });
      });
    },
  });
  caller.abort();
  await assert.rejects(pending, /cancelled/);
  assert.equal(observedSignal.aborted, true);
});

test("turns timed-out requests into an actionable error", async () => {
  await assert.rejects(
    fetchServerInfo({
      timeoutMs: 5,
      fetchImpl: (_url, { signal }) => new Promise((_resolve, reject) => {
        signal.addEventListener("abort", () => reject(new Error("aborted")), { once: true });
      }),
    }),
    /timed out/,
  );
});
