import React, { useEffect, useState } from "react";
import { createRoot } from "react-dom/client";
import { fetchServerInfo } from "./serverInfo.js";
import "./styles.css";

async function postJson(path, body, token) {
  const headers = { "Content-Type": "application/json" };
  if (token) headers.Authorization = `Bearer ${token}`;
  const response = await fetch(path, {
    method: "POST",
    headers,
    cache: "no-store",
    body: JSON.stringify(body),
  });
  if (!response.ok) {
    const error = new Error(`Request was rejected (HTTP ${response.status}).`);
    error.status = response.status;
    throw error;
  }
  if (response.status === 204) return null;
  return response.json();
}

function newEnrollmentSecret() {
  if (!globalThis.crypto?.getRandomValues) {
    throw new Error("Secure browser context is required for enrollment.");
  }
  const bytes = new Uint8Array(32);
  globalThis.crypto.getRandomValues(bytes);
  return Array.from(bytes, (byte) => byte.toString(16).padStart(2, "0")).join("");
}

const emptyJoin = { state: "idle", requestId: null, secret: null, error: null };

function App() {
  const [server, setServer] = useState({ state: "checking" });
  const [requestId, setRequestId] = useState(0);
  const [inviteToken, setInviteToken] = useState("");
  const [username, setUsername] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [deviceName, setDeviceName] = useState("");
  const [join, setJoin] = useState(emptyJoin);
  const [session, setSession] = useState(null);

  useEffect(() => {
    const controller = new AbortController();
    setServer({ state: "checking" });

    fetchServerInfo({ signal: controller.signal })
      .then((data) => {
        if (!controller.signal.aborted) setServer({ state: "ready", data });
      })
      .catch((error) => {
        if (!controller.signal.aborted) {
          setServer({
            state: "offline",
            error: error instanceof Error ? error.message : "Status request failed.",
          });
        }
      });

    return () => controller.abort();
  }, [requestId]);

  useEffect(() => {
    const onOnline = () => setRequestId((id) => id + 1);
    window.addEventListener("online", onOnline);
    return () => window.removeEventListener("online", onOnline);
  }, []);

  const checking = server.state === "checking";
  const offline = server.state === "offline";
  const configured = server.state === "ready" && server.data.configured;
  const heading = checking
    ? "Checking server"
    : offline
      ? "Server unavailable"
      : server.data.serverName;
  const detail = checking
    ? "Reading /api/v1/server/info"
    : offline
      ? server.error
      : `API ${server.data.apiVersion} · ${server.data.runtimeMode}`;
  const busy = join.state === "submitting" || join.state === "exchanging";

  async function submitJoin(event) {
    event.preventDefault();
    if (!configured || busy || session) return;

    setJoin({ ...emptyJoin, state: "submitting" });
    try {
      const secret = newEnrollmentSecret();
      const submitted = await postJson("/api/v1/join", {
        inviteToken: inviteToken.trim(),
        username: username.trim(),
        displayName: displayName.trim(),
        deviceName: deviceName.trim(),
        platform: "Browser",
        enrollmentSecret: secret,
      });
      if (!submitted?.requestId) {
        throw new Error("The server returned no enrollment request identifier.");
      }
      // Keep the one-time secret in this tab only, never in browser storage.
      setJoin({ state: "pending", requestId: submitted.requestId, secret, error: null });
      setInviteToken("");
    } catch (error) {
      setJoin({
        ...emptyJoin,
        error: error instanceof Error ? error.message : "Could not submit the join request.",
      });
    }
  }

  async function exchangeApproval() {
    if (!join.requestId || !join.secret || busy) return;
    const request = join;
    setJoin({ ...join, state: "exchanging", error: null });
    try {
      const approved = await postJson("/api/v1/join/exchange", {
        requestId: request.requestId,
        enrollmentSecret: request.secret,
      });
      if (!approved?.bearerToken || !approved?.deviceId || !approved?.userId) {
        throw new Error("The server returned an incomplete authorized session.");
      }
      // This first browser slice deliberately does not implement persistent pairing.
      // In particular, credentials are not saved to localStorage or URL fragments.
      setSession({
        bearerToken: approved.bearerToken,
        deviceId: approved.deviceId,
        userId: approved.userId,
        expiresUtc: approved.expiresUtc,
      });
      setJoin({ ...emptyJoin, state: "connected" });
    } catch (error) {
      setJoin({
        ...request,
        state: "pending",
        error: error?.status === 401
          ? "Not approved yet, or the enrollment request was rejected. Ask the owner and retry."
          : error instanceof Error ? error.message : "Could not exchange approval.",
      });
    }
  }

  async function logout() {
    if (!session) return;
    let error = null;
    try {
      await postJson("/api/v1/auth/logout", {}, session.bearerToken);
    } catch {
      error = "Local session cleared. Remote revocation could not be confirmed.";
    } finally {
      setSession(null);
      setJoin({ ...emptyJoin, error });
    }
  }

  return (
    <main className="shell">
      <section className="hero" aria-labelledby="client-heading">
        <div className="eyebrow">INTER-LAN · WEB CLIENT</div>
        <h1 id="client-heading">Communication stays on your LAN.</h1>
        <p className="lede">
          Connect through your owner's server. Android and other browsers are clients;
          the owner machine retains server authority.
        </p>

        <div className="status-card" role="status" aria-live="polite" aria-atomic="true">
          <span className={`dot ${server.state}`} aria-hidden="true" />
          <div className="status-content">
            <strong>{heading}</strong>
            <p>{detail}</p>
            {offline && (
              <button
                type="button"
                className="retry-button"
                onClick={() => setRequestId((id) => id + 1)}
              >
                Retry connection
              </button>
            )}
          </div>
        </div>

        {configured && !session && join.state !== "pending" && join.state !== "exchanging" && (
          <form className="join-panel" onSubmit={submitJoin}>
            <h2>Request to join</h2>
            <p>Ask the server owner for a one-time invitation. Your request must be approved.</p>
            <label>
              Invitation token
              <input required autoComplete="off" value={inviteToken} onChange={(e) => setInviteToken(e.target.value)} />
            </label>
            <label>
              Username
              <input required maxLength={80} autoComplete="username" value={username} onChange={(e) => setUsername(e.target.value)} />
            </label>
            <label>
              Display name
              <input required maxLength={120} value={displayName} onChange={(e) => setDisplayName(e.target.value)} />
            </label>
            <label>
              Device name
              <input required maxLength={120} value={deviceName} onChange={(e) => setDeviceName(e.target.value)} placeholder="e.g. Office tablet" />
            </label>
            <button type="submit" disabled={busy}>
              {busy ? "Submitting…" : "Submit join request"}
            </button>
          </form>
        )}

        {(join.state === "pending" || join.state === "exchanging") && (
          <div className="join-panel" role="status">
            <h2>Waiting for owner approval</h2>
            <p>Your request was submitted. Ask the server owner to approve it, then exchange the approval below.</p>
            <button type="button" onClick={exchangeApproval} disabled={busy}>
              {busy ? "Checking approval…" : "Check approval"}
            </button>
            <p className="notice">Keep this tab open until approval is exchanged. Reloading discards the one-time enrollment secret.</p>
          </div>
        )}

        {session && (
          <div className="join-panel" role="status">
            <h2>Device session connected</h2>
            <p>Server-approved browser identity is active until session expiration or logout.</p>
            <p className="notice">Browser persistence and the messaging workspace are not connected in this slice; reloading ends this tab's local session.</p>
            <button type="button" onClick={logout}>Log out and revoke session</button>
          </div>
        )}

        {server.state === "ready" && !server.data.configured && (
          <p className="notice">This server has not completed owner bootstrap. Ask its owner to finish setup first.</p>
        )}

        {join.error && <p className="error-text" role="alert">{join.error}</p>}
        <small>Approval and session exchange are handled by the LAN owner server, not by this browser.</small>
      </section>
    </main>
  );
}

createRoot(document.getElementById("root")).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
);
