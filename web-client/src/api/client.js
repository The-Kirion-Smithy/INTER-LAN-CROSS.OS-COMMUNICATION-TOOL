// All requests stay on the serving LAN origin. Credentials are never persisted here.
export class ApiError extends Error {
  constructor(status, message) {
    super(message);
    this.name = "ApiError";
    this.status = status;
  }
}

export function explainApiError(error) {
  if (error instanceof ApiError) {
    if (error.status === 401) return "Your session has expired or was revoked. Sign in again.";
    if (error.status === 403) return "You do not currently have permission to access this resource. Refresh or ask the owner to review your access.";
    if (error.status === 404) return "That conversation or resource is no longer available to this account.";
    if (error.status === 409) return "The server reported a conflict. Refresh before trying again.";
    if (error.status === 429) return "Too many requests. Wait briefly before retrying.";
    if (error.status >= 500) return "The owner server could not complete the request. Try again when it recovers.";
    return error.message;
  }
  if (error?.name === "AbortError") return "The request was interrupted.";
  return navigator.onLine === false
    ? "This browser is offline. Reconnect to your LAN and retry."
    : "Could not reach the owner server. Check your LAN connection and certificate trust.";
}

export async function requestJson(path, { method = "GET", token, body, signal, timeoutMs = 12000 } = {}) {
  if (typeof path !== "string" || !path.startsWith("/api/v1/") || path.startsWith("//")) {
    throw new TypeError("Only same-origin InterLan API paths are allowed.");
  }
  const url = new URL(path, window.location.origin);
  if (url.origin !== window.location.origin || !url.pathname.startsWith("/api/v1/")) {
    throw new TypeError("Cross-origin API requests are not allowed.");
  }
  const controller = new AbortController();
  const onAbort = () => controller.abort();
  if (signal?.aborted) controller.abort();
  else signal?.addEventListener("abort", onAbort, { once: true });
  let timedOut = false;
  const timer = setTimeout(() => { timedOut = true; controller.abort(); }, timeoutMs);
  try {
    const response = await fetch(url.pathname + url.search, {
      method, signal: controller.signal, cache: "no-store",
      credentials: "same-origin",
      headers: {
        Accept: "application/json",
        ...(body !== undefined ? { "Content-Type": "application/json" } : {}),
        ...(token ? { Authorization: "Bearer " + token } : {})
      },
      ...(body !== undefined ? { body: JSON.stringify(body) } : {})
    });
    if (!response.ok) {
      // Server error bodies can contain internal data. Do not expose or log them.
      throw new ApiError(response.status, "Server rejected the request (HTTP " + response.status + ").");
    }
    if (response.status === 204) return null;
    return response.json();
  } catch (error) {
    if (timedOut) throw new ApiError(408, "The request timed out.");
    throw error;
  } finally {
    clearTimeout(timer);
    signal?.removeEventListener("abort", onAbort);
  }
}

const encode = encodeURIComponent;
const idPath = (id) => {
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id || "")) {
    throw new TypeError("Invalid resource identifier.");
  }
  return id;
};

export const api = {
  join: (body, signal) => requestJson("/api/v1/join", { method: "POST", body, signal }),
  exchange: (body, signal) => requestJson("/api/v1/join/exchange", { method: "POST", body, signal }),
  logout: (token) => requestJson("/api/v1/auth/logout", { method: "POST", token, body: {} }),
  sessions: (token, signal) => requestJson("/api/v1/auth/sessions", { token, signal }),
  currentDevice: (token, signal) => requestJson("/api/v1/auth/device/current", { token, signal }),
  revokeOthers: (token) => requestJson("/api/v1/auth/sessions/revoke-others", { method: "POST", token, body: {} }),
  users: (token, q, signal) => requestJson("/api/v1/users/search?q=" + encode(q) + "&limit=50", { token, signal }),
  inbox: (token, signal) => requestJson("/api/v1/direct/summaries?includeArchived=true", { token, signal }),
  openDirect: (token, otherUserId) => requestJson("/api/v1/direct", { method: "POST", token, body: { otherUserId: idPath(otherUserId) } }),
  recent: (token, id, before, signal) => requestJson("/api/v1/direct/" + idPath(id) + "/messages/recent?limit=40" + (before ? "&beforeMessageId=" + encode(idPath(before)) : ""), { token, signal }),
  catchUp: (token, id, after, signal) => requestJson("/api/v1/direct/" + idPath(id) + "/messages/page?limit=100" + (after ? "&afterMessageId=" + encode(idPath(after)) : ""), { token, signal }),
  send: (token, id, body) => requestJson("/api/v1/direct/" + idPath(id) + "/messages", { method: "POST", token, body }),
  markRead: (token, id, upToMessageId) => requestJson("/api/v1/direct/" + idPath(id) + "/read", { method: "POST", token, body: { upToMessageId: upToMessageId || null } }),
  getPreference: (token, id, signal) => requestJson("/api/v1/direct/" + idPath(id) + "/preference", { token, signal }),
  preference: (token, id, body) => requestJson("/api/v1/direct/" + idPath(id) + "/preference", { method: "PUT", token, body }),
  searchThread: (token, id, query, signal) => requestJson("/api/v1/direct/" + idPath(id) + "/search?q=" + encode(query) + "&limit=40", { token, signal }),
  ticket: (token) => requestJson("/api/v1/realtime/ticket", { method: "POST", token, body: {} })
};
