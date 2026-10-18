import { api, explainApiError } from "../api/client.js";

// SignalR JSON over WebSockets, using a fresh, single-use ticket for each connection.
// No bearer or device renewal credential is placed in a URL.
const RECORD_SEPARATOR = "\u001e";
const methods = new Set([
  "MessageCreated", "MessageEdited", "MessageDeleted",
  "ReceiptUpdated", "TypingChanged", "PresenceChanged"
]);

export function createRealtime({ token, onEvent, onState, onCatchUp, onUnauthorized }) {
  let closed = false;
  let socket = null;
  let generation = 0;
  let retryTimer = null;
  let pingTimer = null;
  let attempt = 0;
  let connectedOnce = false;
  let connected = false;
  let buffer = "";

  const status = (state, detail) => { if (!closed) onState(state, detail); };
  const clearTimers = () => {
    clearTimeout(retryTimer);
    clearInterval(pingTimer);
    retryTimer = null;
    pingTimer = null;
  };

  function scheduleRetry() {
    if (closed) return;
    connected = false;
    clearTimers();
    const delay = document.hidden ? 15000 : Math.min(30000, 1400 * (2 ** Math.min(attempt++, 5)));
    status(navigator.onLine === false ? "offline" : "reconnecting");
    retryTimer = setTimeout(connect, delay);
  }

  async function connect() {
    if (closed) return;
    clearTimers();
    if (navigator.onLine === false) { scheduleRetry(); return; }
    const current = ++generation;
    status(connectedOnce ? "reconnecting" : "connecting");
    try {
      const issued = await api.ticket(token);
      if (closed || current !== generation) return;
      if (!issued?.ticket || Date.parse(issued.expiresUtc) <= Date.now()) throw new Error("Realtime ticket expired.");
      const hub = new URL("/hubs/chat", window.location.origin);
      hub.protocol = window.location.protocol === "https:" ? "wss:" : "ws:";
      // The server consumes the ticket at handshake. Never reuse on retry.
      hub.searchParams.set("ticket", issued.ticket);
      const ws = new WebSocket(hub);
      socket = ws;
      buffer = "";
      let handshaken = false;
      ws.onopen = () => {
        if (closed || current !== generation) { ws.close(); return; }
        ws.send(JSON.stringify({ protocol: "json", version: 1 }) + RECORD_SEPARATOR);
      };
      ws.onmessage = (event) => {
        if (closed || current !== generation || typeof event.data !== "string") return;
        buffer += event.data;
        if (buffer.length > 1024 * 1024) { ws.close(); return; }
        let separator;
        while ((separator = buffer.indexOf(RECORD_SEPARATOR)) !== -1) {
          const raw = buffer.slice(0, separator);
          buffer = buffer.slice(separator + 1);
          if (!raw) continue;
          let data;
          try { data = JSON.parse(raw); } catch { ws.close(); return; }
          if (!handshaken) {
            if (data.error) { ws.close(); return; }
            handshaken = true;
            connected = true;
            attempt = 0;
            status("connected");
            if (connectedOnce) onCatchUp();
            connectedOnce = true;
            pingTimer = setInterval(() => {
              if (ws.readyState === WebSocket.OPEN) ws.send('{"type":6}' + RECORD_SEPARATOR);
            }, 20000);
            continue;
          }
          if (data.type === 7) { ws.close(); return; }
          if (data.type === 1 && methods.has(data.target) && Array.isArray(data.arguments)) {
            onEvent(data.target, data.arguments[0]);
          }
        }
      };
      ws.onerror = () => { /* close schedules a bounded fresh-ticket retry */ };
      ws.onclose = () => {
        if (closed || current !== generation) return;
        connected = false;
        clearInterval(pingTimer);
        scheduleRetry();
      };
    } catch (error) {
      if (closed || current !== generation) return;
      if (error?.status === 401 || error?.status === 403) {
        status("unauthorized", explainApiError(error));
        onUnauthorized();
      } else {
        scheduleRetry();
      }
    }
  }

  function wake() {
    if (closed || socket?.readyState === WebSocket.OPEN || document.hidden) return;
    clearTimeout(retryTimer);
    connect();
  }

  window.addEventListener("online", wake);
  document.addEventListener("visibilitychange", wake);
  connect();

  return {
    get connected() { return connected; },
    setTyping(conversationId, isTyping) {
      if (!connected || socket?.readyState !== WebSocket.OPEN) return false;
      socket.send(JSON.stringify({
        type: 1, target: "SetTyping", arguments: [conversationId, Boolean(isTyping)]
      }) + RECORD_SEPARATOR);
      return true;
    },
    stop() {
      if (closed) return;
      closed = true;
      ++generation;
      clearTimers();
      window.removeEventListener("online", wake);
      document.removeEventListener("visibilitychange", wake);
      connected = false;
      if (socket) {
        socket.onclose = null;
        socket.close();
        socket = null;
      }
    }
  };
}
