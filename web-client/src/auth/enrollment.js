import { api } from "../api/client.js";

export function secureId() {
  if (!globalThis.crypto?.randomUUID) throw new Error("HTTPS or a secure browser context is required.");
  return globalThis.crypto.randomUUID();
}

export function newEnrollmentSecret() {
  if (!globalThis.crypto?.getRandomValues) throw new Error("HTTPS or a secure browser context is required.");
  const bytes = new Uint8Array(32);
  globalThis.crypto.getRandomValues(bytes);
  return Array.from(bytes, byte => byte.toString(16).padStart(2, "0")).join("");
}

export async function submitEnrollment(details, signal) {
  const secret = newEnrollmentSecret();
  const payload = await api.join({
    inviteToken: details.inviteToken.trim(),
    username: details.username.trim(),
    displayName: details.displayName.trim(),
    deviceName: details.deviceName.trim(),
    platform: "Browser",
    enrollmentSecret: secret
  }, signal);
  if (!payload?.requestId) throw new Error("Owner server did not issue a request ID.");
  return { requestId: payload.requestId, secret };
}

export async function exchangeEnrollment(request, signal) {
  const payload = await api.exchange({ requestId: request.requestId, enrollmentSecret: request.secret }, signal);
  if (!payload?.bearerToken || !payload?.userId || !payload?.deviceId || !payload?.expiresUtc || !payload?.sessionId) {
    throw new Error("Owner server issued an incomplete session.");
  }
  if (!(Date.parse(payload.expiresUtc) > Date.now())) throw new Error("Owner server issued an expired session.");
  // Device renewal credentials are deliberately not retained by the browser.
  return Object.freeze({
    bearerToken: payload.bearerToken,
    userId: payload.userId,
    deviceId: payload.deviceId,
    sessionId: payload.sessionId,
    role: payload.role,
    expiresUtc: payload.expiresUtc
  });
}
