import assert from "node:assert/strict";
import test from "node:test";
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { verifyWebAssets } from "./verify.mjs";

const validHtml = [
  "<!doctype html><html><head>",
  '<link rel="stylesheet" href="/client/assets/index-a1.css">',
  "</head><body>",
  '<script type="module" src="/client/assets/index-b2.js"></script>',
  "</body></html>",
].join("");

function fixture(t, html = validHtml) {
  const root = mkdtempSync(join(tmpdir(), "interlan-assets-"));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  mkdirSync(join(root, "assets"));
  if (html !== null) writeFileSync(join(root, "index.html"), html);
  writeFileSync(join(root, "assets", "index-b2.js"), "export const ready = true;");
  writeFileSync(join(root, "assets", "index-a1.css"), "body { margin: 0; }");
  return root;
}

test("accepts an index referencing nonempty local JS and CSS bundles", (t) => {
  assert.deepEqual(verifyWebAssets(fixture(t)), { verifiedAssets: 2 });
});

test("rejects an output directory without index.html", (t) => {
  assert.throws(() => verifyWebAssets(fixture(t, null)), /Missing web-client index/);
});

test("rejects uncompiled source entrypoints", (t) => {
  const html = validHtml.replace("</body>", '<script src="/src/main.jsx"></script></body>');
  assert.throws(() => verifyWebAssets(fixture(t, html)), /not bundled/);
});

test("rejects HTML that omits a JavaScript bundle", (t) => {
  const html = validHtml.replace('<script type="module" src="/client/assets/index-b2.js"></script>', "");
  assert.throws(() => verifyWebAssets(fixture(t, html)), /Missing bundled JavaScript/);
});

test("rejects HTML that omits CSS bundles", (t) => {
  const html = validHtml.replace('<link rel="stylesheet" href="/client/assets/index-a1.css">', "");
  assert.throws(() => verifyWebAssets(fixture(t, html)), /Missing bundled CSS/);
});

test("rejects missing referenced assets", (t) => {
  const html = validHtml.replace("index-b2.js", "missing.js");
  assert.throws(() => verifyWebAssets(fixture(t, html)), /Missing bundled asset/);
});

test("rejects empty referenced assets", (t) => {
  const root = fixture(t);
  writeFileSync(join(root, "assets", "index-b2.js"), "");
  assert.throws(() => verifyWebAssets(root), /Empty bundled asset/);
});

test("rejects traversal out of the Vite assets prefix", (t) => {
  const html = validHtml.replace("/client/assets/index-b2.js", "/client/assets/../outside.js");
  assert.throws(() => verifyWebAssets(fixture(t, html)), /Invalid web asset path/);
});


test("rejects externally hosted JavaScript referenced by HTML", (t) => {
  const html = validHtml.replace(
    "/client/assets/index-b2.js",
    "https://cdn.example.invalid/runtime.js",
  );
  assert.throws(() => verifyWebAssets(fixture(t, html)), /Unexpected script\/style origin/);
});

test("rejects externally hosted stylesheets", (t) => {
  const html = validHtml.replace(
    "/client/assets/index-a1.css",
    "https://cdn.example.invalid/theme.css",
  );
  assert.throws(() => verifyWebAssets(fixture(t, html)), /Unexpected script\/style origin/);
});

test("rejects root-relative JS outside the bundled client directory", (t) => {
  const html = validHtml.replace("/client/assets/index-b2.js", "/scripts/runtime.js");
  assert.throws(() => verifyWebAssets(fixture(t, html)), /Unexpected script\/style origin/);
});

test("rejects protocol-relative script sources", (t) => {
  const html = validHtml.replace("/client/assets/index-b2.js", "//cdn.example.invalid/x.js");
  assert.throws(() => verifyWebAssets(fixture(t, html)), /Unexpected script\/style origin/);
});
