import assert from "node:assert/strict";
import test from "node:test";
import { groupsApi } from "./api/groups.js";

const groupId = "11111111-2222-4333-8444-555555555555";
const messageId = "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee";

test("recent group history uses authenticated same-origin bounded API requests", async () => {
  const originalWindow = globalThis.window;
  const originalFetch = globalThis.fetch;
  const observed = [];
  globalThis.window = { location: { origin: "https://owner.local" } };
  globalThis.fetch = async (url, options) => {
    observed.push({ url, options });
    return { ok: true, status: 200, json: async () => ({
      items: [], olderBeforeMessageId: null, hasOlder: false
    }) };
  };
  try {
    const page = await groupsApi.recent("secret-session-token", groupId, null);
    assert.deepEqual(page.items, []);
    assert.equal(observed[0].url, "/api/v1/groups/" + groupId + "/messages/recent?limit=50");
    assert.equal(observed[0].options.headers.Authorization, "Bearer secret-session-token");
    assert.equal(observed[0].options.credentials, "same-origin");
    assert.equal(observed[0].options.cache, "no-store");
    assert.ok(!observed[0].url.includes("secret-session-token"));

    await groupsApi.recent("secret-session-token", groupId, messageId);
    assert.equal(observed[1].url,
      "/api/v1/groups/" + groupId + "/messages/recent?limit=50&beforeMessageId=" + messageId);

    await groupsApi.messages("secret-session-token", groupId, messageId);
    assert.equal(observed[2].url,
      "/api/v1/groups/" + groupId + "/messages?limit=100&afterMessageId=" + messageId);
  } finally {
    if (originalWindow === undefined) delete globalThis.window;
    else globalThis.window = originalWindow;
    globalThis.fetch = originalFetch;
  }
});

test("recent group client fails closed on malformed group and history cursor IDs", () => {
  const bad = ["", "../outside", "not-a-uuid", null, 42, {}, "11111111-2222-4333-8444-555555555555%2F"];
  for (const candidate of bad) {
    assert.throws(() => groupsApi.recent("secret", candidate, null), TypeError);
    assert.throws(() => groupsApi.recent("secret", groupId, candidate || "invalid"), TypeError);
  }
});
