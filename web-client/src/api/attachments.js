import { ApiError, requestJson } from "./client.js";

// Same-origin API and server-issued bearer token only. Filenames remain inert
// display metadata; clients never send or choose a server filesystem path.
const uuid = id => {
  if (typeof id !== "string" || !/^[\da-f]{8}-[\da-f]{4}-[\da-f]{4}-[\da-f]{4}-[\da-f]{12}$/i.test(id)) {
    throw new TypeError("Invalid attachment, group or conversation identifier.");
  }
  return id;
};
const scopePath = (scopeType, scopeId) => {
  const scope = String(scopeType).toUpperCase();
  if (scope !== "GROUP" && scope !== "DIRECT") throw new TypeError("Invalid attachment scope.");
  return "/api/v1/" + (scope === "GROUP" ? "groups/" : "direct/") + uuid(scopeId) + "/attachments";
};
const attachmentPath = (id) => "/api/v1/attachments/" + uuid(id);

// HTTP headers require safe single-line ASCII. The server also normalizes
// filename metadata and forces downloaded content to attachment disposition.
function displayFilename(name) {
  const cleaned = String(name || "attachment").replace(/[\\/\r\n\x00-\x1f\x7f-\xff]/g, "_").slice(0, 160).trim();
  return cleaned || "attachment";
}

async function binaryRequest(path, { token, method = "GET", file, signal, maxBytes = 10 * 1024 * 1024, timeoutMs = 120000 } = {}) {
  if (typeof token !== "string" || !token) throw new TypeError("Authenticated session required.");
  if (!path.startsWith("/api/v1/") || path.includes("..")) throw new TypeError("Invalid API path.");
  const headers = { Authorization: "Bearer " + token, Accept: method === "POST" ? "application/json" : "application/octet-stream" };
  if (method === "POST") {
    if (!(file instanceof Blob) || file.size < 1 || file.size > maxBytes) {
      throw new RangeError("The file must be nonempty and within the server upload limit.");
    }
    headers["Content-Type"] = "application/octet-stream";
    headers["X-InterLan-Filename"] = displayFilename(file.name);
  }
  if (!Number.isFinite(timeoutMs) || timeoutMs <= 0) throw new RangeError("Invalid transfer timeout.");
  const controller = new AbortController();
  const cancel = () => controller.abort();
  if (signal?.aborted) controller.abort();
  else signal?.addEventListener("abort", cancel, { once: true });
  let timedOut = false;
  const timer = setTimeout(() => { timedOut = true; controller.abort(); }, timeoutMs);
  try {
    const response = await fetch(path, {
      method, headers, body: method === "POST" ? file : undefined,
      signal: controller.signal, cache: "no-store", credentials: "same-origin"
    });
    if (!response.ok) throw new ApiError(response.status, "Attachment request failed (HTTP " + response.status + ").");
    if (method === "POST") return response.json();
    const length = Number(response.headers.get("content-length"));
    if (Number.isFinite(length) && length > maxBytes) throw new RangeError("Attachment response exceeds allowed size.");
    const blob = await response.blob();
    if (blob.size > maxBytes) throw new RangeError("Attachment response exceeds allowed size.");
    return blob;
  } catch (error) {
    if (timedOut) throw new ApiError(408, "The attachment transfer timed out.");
    throw error;
  } finally {
    clearTimeout(timer);
    signal?.removeEventListener("abort", cancel);
  }
}

export const attachmentsApi = {
  policy: (token, signal) => requestJson("/api/v1/attachments/policy", { token, signal }),
  metadata: (token, attachmentId, signal) =>
    requestJson(attachmentPath(attachmentId) + "/metadata", { token, signal }),
  list: (token, scopeType, scopeId, beforeAttachmentId, signal) =>
    requestJson(scopePath(scopeType, scopeId) + "?limit=50" +
      (beforeAttachmentId ? "&beforeAttachmentId=" + uuid(beforeAttachmentId) : ""),
      { token, signal }),
  async upload(token, scopeType, scopeId, file, signal) {
    // Server policy is authoritative, even when the browser detects size early.
    const policy = await attachmentsApi.policy(token, signal);
    const limit = Number(policy?.maxUploadBytes);
    if (!Number.isSafeInteger(limit) || limit <= 0) throw new TypeError("Invalid server upload policy.");
    return binaryRequest(scopePath(scopeType, scopeId), {
      token, method: "POST", file, signal, maxBytes: limit
    });
  },
  download: (token, attachmentId, signal) =>
    binaryRequest(attachmentPath(attachmentId) + "/content", { token, signal })
};
