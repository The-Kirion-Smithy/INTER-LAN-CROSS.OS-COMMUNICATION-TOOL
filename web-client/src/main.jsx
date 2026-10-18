import React, { useCallback, useEffect, useRef, useState } from "react";
import { createRoot } from "react-dom/client";
import { fetchServerInfo } from "./serverInfo.js";
import { api, explainApiError } from "./api/client.js";
import { submitEnrollment, exchangeEnrollment } from "./auth/enrollment.js";
import Workspace from "./Workspace.jsx";
import "./styles.css";

const emptyJoin = { state: "idle", request: null, error: "" };
const emptyForm = { inviteToken: "", username: "", displayName: "", deviceName: "" };

function App() {
  const [server, setServer] = useState({ state: "checking" });
  const [refresh, setRefresh] = useState(0);
  const [form, setForm] = useState(emptyForm);
  const [join, setJoin] = useState(emptyJoin);
  const [session, setSession] = useState(null);
  const [notice, setNotice] = useState("");
  const operation = useRef(null);
  const busy = join.state === "submitting" || join.state === "exchanging";

  useEffect(() => {
    const controller = new AbortController();
    setServer({ state: "checking" });
    fetchServerInfo({ signal: controller.signal })
      .then(data => { if (!controller.signal.aborted) setServer({ state: "ready", data }); })
      .catch(error => {
        if (!controller.signal.aborted) setServer({
          state: "offline", error: error instanceof Error ? error.message : "Server unavailable."
        });
      });
    return () => controller.abort();
  }, [refresh]);

  useEffect(() => {
    const onOnline = () => setRefresh(previous => previous + 1);
    window.addEventListener("online", onOnline);
    return () => window.removeEventListener("online", onOnline);
  }, []);

  const onSessionLost = useCallback((message) => {
    setSession(null);
    setJoin(emptyJoin);
    setNotice(message || "The browser session ended.");
  }, []);

  useEffect(() => {
    if (!session) return;
    const until = Date.parse(session.expiresUtc) - Date.now();
    if (until <= 0) { onSessionLost("The session expired. Request a new approval to continue."); return; }
    const timer = setTimeout(() => onSessionLost("The session expired. Request a new approval to continue."), until);
    return () => clearTimeout(timer);
  }, [session, onSessionLost]);

  useEffect(() => () => operation.current?.abort(), []);

  function cancelEnrollment() {
    operation.current?.abort();
    operation.current = null;
    setJoin(emptyJoin);
    setNotice("Enrollment cancelled in this tab. The owner may still see the submitted request.");
  }

  async function submitJoin(event) {
    event.preventDefault();
    if (server.state !== "ready" || !server.data.configured || session || busy) return;
    const controller = new AbortController();
    operation.current = controller;
    setNotice("");
    setJoin({ state: "submitting", request: null, error: "" });
    try {
      const request = await submitEnrollment(form, controller.signal);
      if (controller.signal.aborted) return;
      setJoin({ state: "pending", request, error: "" });
      setForm(previous => ({ ...previous, inviteToken: "" }));
    } catch (error) {
      if (!controller.signal.aborted) setJoin({
        state: "idle", request: null, error: explainApiError(error)
      });
    } finally {
      if (operation.current === controller) operation.current = null;
    }
  }

  async function exchangeApproval() {
    if (!join.request || busy) return;
    const currentRequest = join.request;
    const controller = new AbortController();
    operation.current = controller;
    setJoin({ state: "exchanging", request: currentRequest, error: "" });
    try {
      const authorized = await exchangeEnrollment(currentRequest, controller.signal);
      if (controller.signal.aborted) return;
      setSession({
        ...authorized,
        displayName: form.displayName.trim(),
        username: form.username.trim(),
        deviceName: form.deviceName.trim()
      });
      setJoin(emptyJoin);
      setNotice("");
    } catch (error) {
      if (!controller.signal.aborted) {
        const errorMessage = error?.status === 401
          ? "Approval is not ready, was denied, or is no longer valid. Check with the owner."
          : explainApiError(error);
        setJoin({ state: "pending", request: currentRequest, error: errorMessage });
      }
    } finally {
      if (operation.current === controller) operation.current = null;
    }
  }

  async function logout() {
    if (!session) return;
    const token = session.bearerToken;
    // Immediately unmount every authenticated view and dispose realtime listeners.
    onSessionLost("Signed out. This tab has cleared its session.");
    try {
      await api.logout(token);
    } catch {
      setNotice("This tab is signed out. Server-side revocation could not be confirmed; ask the owner to revoke the session if needed.");
    }
  }

  if (session) return <Workspace session={session} serverName={server.data?.serverName || "Owner server"}
    onLogout={logout} onSessionLost={onSessionLost} />;

  const configured = server.state === "ready" && server.data.configured;
  return <main className="auth-shell">
    <section className="auth-layout" aria-labelledby="welcome-heading">
      <div className="auth-intro">
        <div className="eyebrow">INTER-LAN / PRIVATE NETWORK MESSAGING</div>
        <div className="auth-logo" aria-hidden="true">I</div>
        <h1 id="welcome-heading">Your people. Your network.</h1>
        <p className="lede">Chat with members on your LAN through the owner-controlled server. Your browser is a client, never the server.</p>
        <div className="status-card" role="status" aria-live="polite">
          <span className={"dot " + server.state} aria-hidden="true" />
          <div className="status-content">
            <strong>{server.state === "ready" ? server.data.serverName : server.state === "checking" ? "Checking the owner server" : "Server unavailable"}</strong>
            <p>{server.state === "ready"
              ? "API " + server.data.apiVersion + " · " + server.data.runtimeMode
              : server.state === "checking" ? "Reading current LAN server status…" : server.error}</p>
            {server.state === "offline" && <button type="button" className="quiet-button" onClick={() => setRefresh(count => count + 1)}>Retry server</button>}
          </div>
        </div>
        <p className="trust-note">If HTTPS shows a certificate warning, contact the server owner and verify its identity through a trusted channel. Browsers do not provide native certificate pinning here.</p>
      </div>
      <div className="auth-card">
        {!configured && server.state === "ready" && <div role="status"><h2>Owner setup required</h2><p>Ask the owner to finish server bootstrap before enrollment.</p></div>}
        {!configured && server.state === "checking" && <div role="status"><h2>Connecting</h2><p>Checking whether the server can enroll devices.</p></div>}
        {!configured && server.state === "offline" && <div role="status"><h2>LAN connection unavailable</h2><p>Reconnect to the owner's LAN or confirm HTTPS trust, then retry.</p></div>}
        {configured && join.state !== "pending" && join.state !== "exchanging" && <form onSubmit={submitJoin} className="enrollment-form">
          <span className="form-kicker">DEVICE ENROLLMENT</span><h2>Request access</h2>
          <p>Enter the one-time invitation shared by the owner. Approval is required before messaging.</p>
          <label>Invitation token<input type="text" required autoComplete="off" value={form.inviteToken}
            onChange={event => setForm(previous => ({ ...previous, inviteToken: event.target.value }))} /></label>
          <label>Username<input required maxLength={80} autoComplete="username" value={form.username}
            onChange={event => setForm(previous => ({ ...previous, username: event.target.value }))} /></label>
          <label>Display name<input required maxLength={120} value={form.displayName}
            onChange={event => setForm(previous => ({ ...previous, displayName: event.target.value }))} /></label>
          <label>Device name<input required maxLength={120} placeholder="Example: Office Android phone" value={form.deviceName}
            onChange={event => setForm(previous => ({ ...previous, deviceName: event.target.value }))} /></label>
          <button type="submit" disabled={busy}> {busy ? "Submitting request…" : "Request owner approval"} </button>
          {busy && <button type="button" className="quiet-button" onClick={cancelEnrollment}>Cancel</button>}
        </form>}
        {configured && (join.state === "pending" || join.state === "exchanging") && <div className="approval-panel" role="status">
          <div className="approval-symbol" aria-hidden="true">⌛</div>
          <h2>Awaiting owner approval</h2><p>Your request was sent to the owner server. After approval, check for your authorized session.</p>
          <button type="button" disabled={busy} onClick={exchangeApproval}>{busy ? "Checking approval…" : "Check approval"}</button>
          <button type="button" className="quiet-button" onClick={cancelEnrollment}>Cancel on this device</button>
          <p className="notice">Keep this tab open. Reloading erases the one-time enrollment secret and any issued session. The browser stores no device credential.</p>
        </div>}
        {join.error && <p className="inline-error" role="alert">{join.error}</p>}
        {notice && <p className="inline-notice" role="status">{notice}</p>}
      </div>
    </section>
    <footer className="auth-footer">INTER-LAN · Server-enforced identity, authorization and message storage</footer>
  </main>;
}

createRoot(document.getElementById("root")).render(<React.StrictMode><App /></React.StrictMode>);
