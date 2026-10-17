import React, { useEffect, useState } from "react";
import { createRoot } from "react-dom/client";
import { fetchServerInfo } from "./serverInfo.js";
import "./styles.css";

function App() {
  const [server, setServer] = useState({ state: "checking" });
  const [requestId, setRequestId] = useState(0);

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

  // A network reconnection can restore the local owner without a page reload.
  useEffect(() => {
    const onOnline = () => setRequestId((id) => id + 1);
    window.addEventListener("online", onOnline);
    return () => window.removeEventListener("online", onOnline);
  }, []);

  const checking = server.state === "checking";
  const offline = server.state === "offline";
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

  return (
    <main className="shell">
      <section className="hero" aria-labelledby="client-heading">
        <div className="eyebrow">INTER-LAN · WEB CLIENT</div>
        <h1 id="client-heading">Communication stays on your LAN.</h1>
        <p className="lede">
          Android uses this responsive client. The owner machine remains the canonical server.
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

        <div className="actions">
          <button type="button" disabled>Create Server</button>
          <button type="button" disabled className="secondary">Join Server</button>
        </div>
        <small>
          Web client preview. Enrollment and messaging controls are not connected yet.
        </small>
      </section>
    </main>
  );
}

createRoot(document.getElementById("root")).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
);
