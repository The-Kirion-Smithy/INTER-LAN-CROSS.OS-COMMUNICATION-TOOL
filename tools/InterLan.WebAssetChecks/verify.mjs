import { existsSync, readFileSync, statSync } from "node:fs";
import { resolve, sep } from "node:path";
import { pathToFileURL } from "node:url";

function requireAsset(condition, message) {
  if (!condition) throw new Error(message);
}

/**
 * Assert that the production HTML references present, nonempty local assets.
 * This is an output-only check: no server process, credentials, or network.
 */
export function verifyWebAssets(outputDirectory) {
  const root = resolve(outputDirectory);
  const indexFile = resolve(root, "index.html");
  requireAsset(existsSync(indexFile), "Missing web-client index.html");

  const html = readFileSync(indexFile, "utf8");
  requireAsset(!html.includes("/src/main.jsx"), "Web entrypoint was not bundled");

  const attributes = [...html.matchAll(/\b(?:src|href)\s*=\s*["']([^"']+)["']/gi)]
    .map((match) => match[1]);
  // A LAN client must not silently load scripts or styles from another origin
  // or bypass the Vite bundle directory.
  const executableAssets = attributes.filter((url) => /\.(?:js|css)(?:[?#]|$)/i.test(url));
  for (const url of executableAssets) {
    requireAsset(url.startsWith("/client/assets/"), `Unexpected script/style origin: ${url}`);
  }
  const references = attributes.filter((url) => url.startsWith("/client/assets/"));

  const scripts = references.filter((url) => /\.js(?:[?#]|$)/i.test(url));
  const styles = references.filter((url) => /\.css(?:[?#]|$)/i.test(url));
  requireAsset(scripts.length > 0, "Missing bundled JavaScript reference");
  requireAsset(styles.length > 0, "Missing bundled CSS reference");

  const checked = new Set();
  for (const reference of references) {
    const url = new URL(reference, "https://interlan.invalid");
    requireAsset(url.origin === "https://interlan.invalid", "External web asset reference");
    requireAsset(url.pathname.startsWith("/client/assets/"), "Invalid web asset path");

    const assetPath = resolve(root, url.pathname.substring("/client/".length));
    requireAsset(assetPath.startsWith(root + sep), "Web asset escapes output directory");
    requireAsset(existsSync(assetPath), `Missing bundled asset: ${url.pathname}`);
    requireAsset(statSync(assetPath).isFile(), `Not a bundled file: ${url.pathname}`);
    requireAsset(statSync(assetPath).size > 0, `Empty bundled asset: ${url.pathname}`);
    checked.add(url.pathname);
  }

  return { verifiedAssets: checked.size };
}

if (process.argv[1] && pathToFileURL(resolve(process.argv[1])).href === import.meta.url) {
  try {
    const outputDirectory = process.argv[2] ?? "src/InterLan.Server/wwwroot/client";
    const result = verifyWebAssets(outputDirectory);
    process.stdout.write(`PASS: verified ${result.verifiedAssets} web assets.\n`);
  } catch (error) {
    process.stderr.write(`FAIL: ${error.message}\n`);
    process.exitCode = 1;
  }
}
