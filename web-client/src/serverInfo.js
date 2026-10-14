const SERVER_INFO_URL = "/api/v1/server/info";

/**
 * Validate the public server-info envelope without copying unrelated metadata
 * (notably the certificate fingerprint) into the UI model.
 */
export function normalizeServerInfo(payload) {
  const server = payload?.server;
  if (!server || typeof server !== "object" || Array.isArray(server)) {
    throw new Error("Server returned an invalid status response.");
  }

  if (typeof server.configured !== "boolean") {
    throw new Error("Server status is missing its configuration state.");
  }

  if (typeof server.apiVersion !== "string" || !server.apiVersion.trim()) {
    throw new Error("Server status is missing its API version.");
  }

  // ASP.NET Core may serialize this enum as an integer or a string.
  const runtimeMode = server.runtimeMode;
  if (
    (typeof runtimeMode !== "string" || !runtimeMode.trim()) &&
    (typeof runtimeMode !== "number" || !Number.isInteger(runtimeMode))
  ) {
    throw new Error("Server status is missing its runtime mode.");
  }

  return {
    configured: server.configured,
    serverName:
      server.configured && typeof server.serverName === "string" && server.serverName.trim()
        ? server.serverName.trim()
        : server.configured
          ? "Unnamed server"
          : "Server not configured",
    apiVersion: server.apiVersion,
    runtimeMode: String(runtimeMode),
  };
}

/**
 * Request a fresh server status. Caller cancellation is linked to the timeout,
 * and both event listeners and the timer are released on completion.
 */
export async function fetchServerInfo({
  fetchImpl = fetch,
  signal,
  timeoutMs = 10_000,
} = {}) {
  if (!Number.isFinite(timeoutMs) || timeoutMs <= 0) {
    throw new RangeError("timeoutMs must be positive.");
  }

  const controller = new AbortController();
  const abortFromCaller = () => controller.abort(signal?.reason);
  if (signal?.aborted) {
    abortFromCaller();
  } else {
    signal?.addEventListener("abort", abortFromCaller, { once: true });
  }

  let timedOut = false;
  const timeout = setTimeout(() => {
    timedOut = true;
    controller.abort();
  }, timeoutMs);

  try {
    const response = await fetchImpl(SERVER_INFO_URL, {
      signal: controller.signal,
      cache: "no-store",
    });
    if (!response.ok) {
      throw new Error(`Server returned HTTP ${response.status}.`);
    }
    return normalizeServerInfo(await response.json());
  } catch (error) {
    if (timedOut) {
      throw new Error("Server status request timed out.", { cause: error });
    }
    throw error;
  } finally {
    clearTimeout(timeout);
    signal?.removeEventListener("abort", abortFromCaller);
  }
}
