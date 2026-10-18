import React, { useCallback, useEffect, useRef, useState } from "react";
import { api, explainApiError } from "./api/client.js";
import { secureId } from "./auth/enrollment.js";
import { createRealtime } from "./realtime/connection.js";

const sortMessages = (a, b) => (Date.parse(a.createdUtc) - Date.parse(b.createdUtc)) ||
  String(a.messageId).localeCompare(String(b.messageId));
function mergeMessages(current, incoming) {
  const items = new Map(current.map(m => [m.messageId, m]));
  for (const message of incoming || []) if (message?.messageId) items.set(message.messageId, message);
  return [...items.values()].sort(sortMessages);
}
const clock = (date) => date ? new Date(date).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }) : "";
const nameOf = (user) => user?.displayName || user?.username || "Unknown user";
const preview = (message) => !message ? "No messages yet" : message.deletedUtc ? "Message deleted" : message.body;
const sections = [["chats", "Chats"], ["people", "People"], ["groups", "Groups"], ["files", "Files"], ["settings", "Security"]];

export default function Workspace({ session, serverName, onLogout, onSessionLost }) {
  const [section, setSection] = useState("chats");
  const [inbox, setInbox] = useState([]);
  const [inboxBusy, setInboxBusy] = useState(true);
  const [inboxError, setInboxError] = useState("");
  const [search, setSearch] = useState("");
  const [people, setPeople] = useState([]);
  const [peopleBusy, setPeopleBusy] = useState(false);
  const [active, setActive] = useState(null);
  const [messages, setMessages] = useState([]);
  const [historyBusy, setHistoryBusy] = useState(false);
  const [hasOlder, setHasOlder] = useState(false);
  const [olderCursor, setOlderCursor] = useState(null);
  const [threadError, setThreadError] = useState("");
  const [draft, setDraft] = useState("");
  const [outgoing, setOutgoing] = useState(null);
  const [realtimeState, setRealtimeState] = useState("connecting");
  const [typing, setTyping] = useState(null);
  const [device, setDevice] = useState(null);
  const [sessions, setSessions] = useState([]);
  const [securityError, setSecurityError] = useState("");
  const [searchError, setSearchError] = useState("");
  const [preference, setPreference] = useState(null);
  const [searchOpen, setSearchOpen] = useState(false);
  const [threadQuery, setThreadQuery] = useState("");
  const [threadMatches, setThreadMatches] = useState([]);
  const [threadSearchBusy, setThreadSearchBusy] = useState(false);
  const [threadSearchError, setThreadSearchError] = useState("");
  const [now, setNow] = useState(Date.now());

  const threadRef = useRef(null);
  const activeRef = useRef(null);
  const latestRef = useRef(null);
  const realtimeRef = useRef(null);
  const typingTimerRef = useRef(0);
  const historyGeneration = useRef(0);
  const sendBusy = useRef(false);
  const refreshTimer = useRef(null);
  const token = session.bearerToken;

  useEffect(() => { activeRef.current = active?.conversationId || null; }, [active]);
  useEffect(() => { latestRef.current = messages.at(-1)?.messageId || null; }, [messages]);
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, []);
  const remaining = Math.max(0, Math.ceil((Date.parse(session.expiresUtc) - now) / 60000));

  const failIfUnauthorized = useCallback((error) => {
    if (error?.status === 401 || error?.status === 403) {
      onSessionLost("The owner server ended or revoked this session.");
      return true;
    }
    return false;
  }, [onSessionLost]);

  const refreshInbox = useCallback(async (signal) => {
    try {
      const data = await api.inbox(token, signal);
      if (!signal?.aborted) {
        setInbox(Array.isArray(data) ? data : []);
        setInboxError("");
      }
    } catch (error) {
      if (!signal?.aborted && !failIfUnauthorized(error)) setInboxError(explainApiError(error));
    } finally {
      if (!signal?.aborted) setInboxBusy(false);
    }
  }, [token, failIfUnauthorized]);

  useEffect(() => {
    const controller = new AbortController();
    refreshInbox(controller.signal);
    return () => controller.abort();
  }, [refreshInbox]);

  const scheduleInbox = useCallback(() => {
    clearTimeout(refreshTimer.current);
    refreshTimer.current = setTimeout(() => refreshInbox(), 250);
  }, [refreshInbox]);

  const catchUp = useCallback(async () => {
    scheduleInbox();
    const id = activeRef.current;
    if (!id) return;
    const generation = historyGeneration.current;
    let cursor = latestRef.current;
    try {
      // Follow bounded pages without confusing an event with persisted history.
      for (let pageNumber = 0; pageNumber < 12; pageNumber++) {
        const page = await api.catchUp(token, id, cursor);
        if (generation !== historyGeneration.current || activeRef.current !== id) return;
        setMessages(previous => mergeMessages(previous, page.items));
        if (!page.items?.length || !page.hasMore || !page.nextAfterMessageId) break;
        if (cursor === page.nextAfterMessageId) break;
        cursor = page.nextAfterMessageId;
      }
    } catch (error) {
      if (!failIfUnauthorized(error)) setThreadError(explainApiError(error));
    }
  }, [token, scheduleInbox, failIfUnauthorized]);

  useEffect(() => {
    const connection = createRealtime({
      token,
      onState: state => setRealtimeState(state),
      onUnauthorized: () => onSessionLost("Realtime authentication was revoked."),
      onCatchUp: catchUp,
      onEvent: (event, payload) => {
        if (event === "MessageCreated" || event === "MessageEdited") {
          if (payload?.scopeType?.toLowerCase() === "direct" && payload.scopeId === activeRef.current) {
            setMessages(previous => mergeMessages(previous, [payload]));
          }
          scheduleInbox();
        } else if (event === "MessageDeleted") {
          if (payload?.conversationId === activeRef.current) {
            setMessages(previous => previous.map(message => message.messageId === payload.messageId
              ? { ...message, deletedUtc: payload.deletedUtc, body: "" } : message));
          }
          scheduleInbox();
        } else if (event === "TypingChanged") {
          if (payload?.conversationId === activeRef.current && payload.userId !== session.userId) {
            setTyping(payload.isTyping ? { userId: payload.userId, until: Date.now() + 4500 } : null);
          }
        } else if (event === "ReceiptUpdated") {
          scheduleInbox();
        }
      }
    });
    realtimeRef.current = connection;
    return () => {
      connection.stop();
      if (realtimeRef.current === connection) realtimeRef.current = null;
      clearTimeout(refreshTimer.current);
    };
  }, [token, catchUp, onSessionLost, scheduleInbox, session.userId]);

  useEffect(() => {
    const timer = setInterval(() => setTyping(previous => previous && previous.until <= Date.now() ? null : previous), 1000);
    return () => clearInterval(timer);
  }, []);

  useEffect(() => {
    if (section !== "people" || !search.trim()) { setPeople([]); setSearchError(""); return; }
    const controller = new AbortController();
    const timer = setTimeout(async () => {
      setPeopleBusy(true);
      try {
        const data = await api.users(token, search.trim(), controller.signal);
        if (!controller.signal.aborted) setPeople(Array.isArray(data?.users) ? data.users : Array.isArray(data) ? data : []);
      } catch (error) {
        if (!controller.signal.aborted && !failIfUnauthorized(error)) setSearchError(explainApiError(error));
      } finally {
        if (!controller.signal.aborted) setPeopleBusy(false);
      }
    }, 300);
    return () => { clearTimeout(timer); controller.abort(); };
  }, [section, search, token, failIfUnauthorized]);

  useEffect(() => {
    if (!active?.conversationId) return;
    const controller = new AbortController();
    setPreference(null);
    setSearchOpen(false);
    setThreadQuery("");
    setThreadMatches([]);
    setThreadSearchError("");
    api.getPreference(token, active.conversationId, controller.signal)
      .then(data => { if (!controller.signal.aborted) setPreference(data); })
      .catch(error => { if (!controller.signal.aborted && !failIfUnauthorized(error)) setThreadError(explainApiError(error)); });
    return () => controller.abort();
  }, [active?.conversationId, token, failIfUnauthorized]);

  async function changePreference(key) {
    if (!active || !preference) return;
    const id = active.conversationId;
    try {
      const next = { isPinned: Boolean(preference.isPinned), mutedUntilUtc: preference.mutedUntilUtc || null, isArchived: Boolean(preference.isArchived) };
      next[key] = !next[key];
      const confirmed = await api.preference(token, id, next);
      if (activeRef.current === id) setPreference(confirmed);
      scheduleInbox();
    } catch (error) {
      if (!failIfUnauthorized(error)) setThreadError(explainApiError(error));
    }
  }

  async function searchConversation(event) {
    event.preventDefault();
    if (!active || !threadQuery.trim()) return;
    setThreadSearchBusy(true);
    setThreadSearchError("");
    try {
      const data = await api.searchThread(token, active.conversationId, threadQuery.trim());
      if (activeRef.current === data.conversationId) setThreadMatches(Array.isArray(data.items) ? data.items : []);
    } catch (error) {
      if (!failIfUnauthorized(error)) setThreadSearchError(explainApiError(error));
    } finally {
      setThreadSearchBusy(false);
    }
  }

  useEffect(() => {
    if (!active?.conversationId) return;
    const controller = new AbortController();
    const generation = ++historyGeneration.current;
    setMessages([]);
    setDraft("");
    setOutgoing(null);
    setHasOlder(false);
    setOlderCursor(null);
    setThreadError("");
    setTyping(null);
    setHistoryBusy(true);
    api.recent(token, active.conversationId, null, controller.signal).then(page => {
      if (controller.signal.aborted || historyGeneration.current !== generation) return;
      setMessages(mergeMessages([], page.items));
      setHasOlder(Boolean(page.hasOlder));
      setOlderCursor(page.olderBeforeMessageId);
      requestAnimationFrame(() => {
        if (threadRef.current) threadRef.current.scrollTop = threadRef.current.scrollHeight;
      });
    }).catch(error => {
      if (!controller.signal.aborted && !failIfUnauthorized(error)) setThreadError(explainApiError(error));
    }).finally(() => {
      if (!controller.signal.aborted && historyGeneration.current === generation) setHistoryBusy(false);
    });
    return () => { controller.abort(); ++historyGeneration.current; };
  }, [active?.conversationId, token, failIfUnauthorized]);

  async function loadOlder() {
    if (!active || !hasOlder || historyBusy || !olderCursor) return;
    const id = active.conversationId;
    const generation = historyGeneration.current;
    const list = threadRef.current;
    const previousHeight = list?.scrollHeight || 0;
    setHistoryBusy(true);
    try {
      const page = await api.recent(token, id, olderCursor);
      if (activeRef.current !== id || generation !== historyGeneration.current) return;
      setMessages(previous => mergeMessages(previous, page.items));
      setHasOlder(Boolean(page.hasOlder));
      setOlderCursor(page.olderBeforeMessageId);
      requestAnimationFrame(() => {
        if (threadRef.current) threadRef.current.scrollTop += threadRef.current.scrollHeight - previousHeight;
      });
    } catch (error) {
      if (!failIfUnauthorized(error)) setThreadError(explainApiError(error));
    } finally {
      setHistoryBusy(false);
    }
  }

  async function openUser(user) {
    if (!user?.userId || user.userId === session.userId) return;
    setSearchError("");
    try {
      const opened = await api.openDirect(token, user.userId);
      if (!opened?.conversationId) throw new Error("Server did not return a conversation.");
      setActive({ ...opened, otherUser: opened.otherUser || user });
      setSection("chats");
      scheduleInbox();
    } catch (error) {
      if (!failIfUnauthorized(error)) setSearchError(explainApiError(error));
    }
  }

  function openThread(conversation) {
    setActive({ conversationId: conversation.conversationId, otherUser: conversation.otherUser });
    setSection("chats");
  }

  async function sendMessage(payload) {
    if (sendBusy.current) return;
    sendBusy.current = true;
    setOutgoing({ ...payload, state: "sending", error: "" });
    try {
      const saved = await api.send(token, payload.conversationId, {
        clientMessageId: payload.clientMessageId,
        body: payload.body
      });
      // A sender bubble becomes 'sent' only when the server confirms persistence.
      if (activeRef.current === payload.conversationId) {
        setMessages(previous => mergeMessages(previous, [saved]));
        requestAnimationFrame(() => {
          if (threadRef.current) threadRef.current.scrollTop = threadRef.current.scrollHeight;
        });
      }
      setOutgoing(null);
      scheduleInbox();
    } catch (error) {
      if (!failIfUnauthorized(error)) setOutgoing({ ...payload, state: "failed", error: explainApiError(error) });
    } finally {
      sendBusy.current = false;
    }
  }

  function submitMessage(event) {
    event.preventDefault();
    if (!active || outgoing || !draft.trim()) return;
    const payload = { conversationId: active.conversationId, clientMessageId: secureId(), body: draft.trim() };
    setDraft("");
    realtimeRef.current?.setTyping(active.conversationId, false);
    sendMessage(payload);
  }

  function onDraft(value) {
    setDraft(value);
    if (!active || !realtimeRef.current?.connected) return;
    if (Date.now() - typingTimerRef.current > 2500) {
      realtimeRef.current.setTyping(active.conversationId, Boolean(value.trim()));
      typingTimerRef.current = Date.now();
    }
  }

  useEffect(() => {
    const id = active?.conversationId;
    const last = messages.at(-1);
    if (!id || !last || section !== "chats" || document.hidden || !navigator.onLine) return;
    if (last.senderUserId === session.userId) return;
    const controller = new AbortController();
    const timer = setTimeout(() => {
      if (!controller.signal.aborted && activeRef.current === id) {
        api.markRead(token, id, last.messageId).then(scheduleInbox).catch(error => {
          if (!failIfUnauthorized(error)) setThreadError(explainApiError(error));
        });
      }
    }, 350);
    return () => { controller.abort(); clearTimeout(timer); };
  }, [active?.conversationId, messages, section, session.userId, token, scheduleInbox, failIfUnauthorized]);

  async function loadSecurity() {
    setSecurityError("");
    const [ownSessions, ownDevice] = await Promise.allSettled([api.sessions(token), api.currentDevice(token)]);
    if (ownSessions.status === "fulfilled") setSessions(Array.isArray(ownSessions.value) ? ownSessions.value : []);
    else if (!failIfUnauthorized(ownSessions.reason)) setSecurityError(explainApiError(ownSessions.reason));
    if (ownDevice.status === "fulfilled") setDevice(ownDevice.value);
  }
  useEffect(() => { if (section === "settings") loadSecurity(); }, [section]);

  async function revokeOthers() {
    try { await api.revokeOthers(token); await loadSecurity(); }
    catch (error) { if (!failIfUnauthorized(error)) setSecurityError(explainApiError(error)); }
  }

  const selected = active && (inbox.find(item => item.conversationId === active.conversationId) || active);
  const unread = inbox.reduce((sum, item) => sum + (Number(item.unreadCount) || 0), 0);
  const orderedInbox = [...inbox].sort((a, b) =>
    Number(Boolean(b.isPinned)) - Number(Boolean(a.isPinned)) ||
    Date.parse(b.lastMessage?.createdUtc || b.createdUtc) - Date.parse(a.lastMessage?.createdUtc || a.createdUtc));

  return (
    <main className="app-shell">
      <header className="app-topbar">
        <div className="brand"><span className="brand-mark" aria-hidden="true">I</span><span>INTER-LAN<small>{serverName} · Browser client</small></span></div>
        <div className="topbar-actions">
          <span className={"connection " + realtimeState} role="status"><span aria-hidden="true" className="connection-dot" />{realtimeState}</span>
          <span className="session-chip" title="Session expiry">Session: {remaining}m</span>
          <button type="button" className="quiet-button" onClick={onLogout}>Log out</button>
        </div>
      </header>
      <div className={"workspace section-" + section + " " + (active && section === "chats" ? "has-thread" : "")}>
        <aside className="workspace-sidebar" aria-label="Workspace navigation">
          <div className="sidebar-heading"><h1>{sections.find(item => item[0] === section)?.[1]}</h1><p>{section === "chats" ? unread + " unread messages" : "Server-authorized workspace"}</p></div>
          <nav className="section-nav" aria-label="Features">{sections.map(([id, label]) =>
            <button type="button" key={id} className={section === id ? "active" : ""} aria-current={section === id ? "page" : undefined}
              onClick={() => { setSection(id); if (id !== "chats") setActive(null); }}>{label}</button>)}</nav>
          {section === "chats" && <div className="sidebar-body">
            <div className="sidebar-toolbar"><span>Conversations</span><button type="button" className="text-button" onClick={() => { setSection("people"); setActive(null); }}>+ New chat</button></div>
            {inboxBusy && <p className="muted">Loading conversations…</p>}
            {inboxError && <p role="alert" className="inline-error">{inboxError} <button type="button" className="text-button" onClick={() => refreshInbox()}>Retry</button></p>}
            {!inboxBusy && orderedInbox.length === 0 && <p className="empty-copy">No conversations yet. Find someone in People to start a direct message.</p>}
            <div className="conversation-list">{orderedInbox.map(item =>
              <button type="button" key={item.conversationId} className={"conversation-row " + (item.conversationId === active?.conversationId ? "chosen" : "")}
                onClick={() => openThread(item)}>
                <span className="avatar" aria-hidden="true">{nameOf(item.otherUser).slice(0, 1).toUpperCase()}</span>
                <span className="conversation-text"><strong>{nameOf(item.otherUser)}{item.isPinned ? " · Pinned" : ""}</strong><span>{preview(item.lastMessage)}</span></span>
                <span className="conversation-meta"><time>{clock(item.lastMessage?.createdUtc)}</time>{item.unreadCount > 0 && <b aria-label={item.unreadCount + " unread"}>{item.unreadCount}</b>}</span>
              </button>)}</div>
          </div>}
          {section === "people" && <div className="sidebar-body">
            <label className="search-label">Find people<input type="search" value={search} onChange={event => setSearch(event.target.value)}
              placeholder="Search username or name" autoComplete="off" /></label>
            {peopleBusy && <p className="muted">Searching server…</p>}
            {searchError && <p className="inline-error" role="alert">{searchError}</p>}
            {!search.trim() && <p className="empty-copy">Search the owner's real user directory. No contacts are stored in this browser.</p>}
            <div className="conversation-list">{people.filter(person => person.userId !== session.userId).map(person =>
              <button type="button" key={person.userId} className="conversation-row" onClick={() => openUser(person)}>
                <span className="avatar" aria-hidden="true">{nameOf(person).slice(0, 1).toUpperCase()}</span>
                <span className="conversation-text"><strong>{nameOf(person)}</strong><span>@{person.username}</span></span><span aria-hidden="true">→</span>
              </button>)}</div>
            {search.trim() && !peopleBusy && people.length === 0 && !searchError && <p className="empty-copy">No matching users.</p>}
          </div>}
          {(section === "groups" || section === "files") && <div className="sidebar-body"><p className="empty-copy">Requires group and attachment APIs from the server. No group memberships or files are invented locally.</p></div>}
          {section === "settings" && <div className="sidebar-body"><p className="empty-copy">Account identity and server session security.</p></div>}
        </aside>

        <section className="workspace-content" aria-label={section === "chats" ? "Conversation" : "Feature details"}>
          {section === "chats" && !selected && <div className="empty-state"><div className="empty-icon" aria-hidden="true">✉</div><h2>Choose a conversation</h2><p>Messages are saved by the owner server. Your browser session is temporary.</p><button type="button" onClick={() => setSection("people")}>Find a person</button></div>}
          {section === "chats" && selected && <>
            <header className="thread-header">
              <button type="button" className="mobile-back" onClick={() => setActive(null)} aria-label="Back to conversations">←</button>
              <span className="avatar" aria-hidden="true">{nameOf(selected.otherUser).slice(0, 1).toUpperCase()}</span>
              <div className="thread-title"><h2>{nameOf(selected.otherUser)}</h2><span>Direct conversation · server-authorized</span></div>
              <button type="button" className="quiet-button" onClick={catchUp} title="Fetch messages missed while disconnected">Sync</button>
            </header>
            <div className="thread-actions" aria-label="Conversation actions">
              <button type="button" className="quiet-button" disabled={!preference} onClick={() => changePreference("isPinned")}>{preference?.isPinned ? "Unpin" : "Pin conversation"}</button>
              <button type="button" className="quiet-button" disabled={!preference} onClick={() => changePreference("isArchived")}>{preference?.isArchived ? "Unarchive" : "Archive"}</button>
              <button type="button" className="quiet-button" aria-expanded={searchOpen} onClick={() => setSearchOpen(value => !value)}>{searchOpen ? "Close search" : "Search messages"}</button>
            </div>
            {searchOpen && <div className="thread-search">
              <form onSubmit={searchConversation}>
                <label className="sr-only" htmlFor="thread-search-query">Search this conversation</label>
                <input id="thread-search-query" type="search" value={threadQuery} onChange={event => setThreadQuery(event.target.value)} placeholder="Search server-saved messages" required />
                <button type="submit" disabled={threadSearchBusy}>Search</button>
              </form>
              {threadSearchError && <p className="inline-error" role="alert">{threadSearchError}</p>}
              <div className="search-hits" role="region" aria-label="Message search results">
                {threadMatches.map(hit => <div className="search-hit" key={hit.messageId}><span>{hit.deletedUtc ? "Deleted message" : hit.body}</span><time>{new Date(hit.createdUtc).toLocaleString()}</time></div>)}
                {threadMatches.length === 0 && !threadSearchBusy && <p className="muted">No results loaded or no matches found.</p>}
              </div>
            </div>}
            <div className="thread-messages" ref={threadRef} role="log" aria-label="Direct messages" aria-live="polite" aria-relevant="additions text">
              {hasOlder && <button type="button" className="load-more" disabled={historyBusy} onClick={loadOlder}>{historyBusy ? "Loading…" : "Load older messages"}</button>}
              {historyBusy && messages.length === 0 && <p className="muted center">Loading history…</p>}
              {threadError && <p className="inline-error" role="alert">{threadError}</p>}
              {!historyBusy && messages.length === 0 && <p className="empty-copy center">No messages yet. Send the first one.</p>}
              {messages.map(message => {
                const mine = message.senderUserId === session.userId;
                return <article className={"message " + (mine ? "mine" : "theirs")} key={message.messageId}>
                  <div className="message-body">{message.deletedUtc ? <em>Message deleted</em> : message.body}</div>
                  <div className="message-meta"><time dateTime={message.createdUtc}>{clock(message.createdUtc)}</time>{message.editedUtc && !message.deletedUtc && " · edited"}{mine && " · sent"}</div>
                </article>;
              })}
              {outgoing?.conversationId === active?.conversationId && <article className="message mine pending-message">
                <div className="message-body">{outgoing.body}</div><div className="message-meta">{outgoing.state === "sending" ? "Sending · awaiting server" : "Not confirmed"}</div>
                {outgoing.state === "failed" && <div className="send-failure"><span role="alert">{outgoing.error}</span><button type="button" onClick={() => sendMessage(outgoing)}>Retry same message</button><button type="button" onClick={() => { setDraft(outgoing.body); setOutgoing(null); }}>Edit draft</button></div>}
              </article>}
            </div>
            <div className="typing-line" aria-live="polite">{typing?.until > now && "Other person is typing…"}</div>
            <form className="composer" onSubmit={submitMessage}>
              <label className="sr-only" htmlFor="message-compose">Message</label>
              <textarea id="message-compose" value={draft} onChange={event => onDraft(event.target.value)} rows={2}
                placeholder="Write a message…" maxLength={4000} disabled={Boolean(outgoing) || remaining === 0}
                onKeyDown={event => { if (event.key === "Enter" && !event.shiftKey && !event.nativeEvent.isComposing) { event.preventDefault(); submitMessage(event); } }} />
              <button type="submit" disabled={!draft.trim() || Boolean(outgoing) || remaining === 0}>Send</button>
            </form>
          </>}
          {section === "people" && <div className="empty-state"><div className="empty-icon" aria-hidden="true">◎</div><h2>Real user directory</h2><p>Search by name or username to create or open a server-authorized direct conversation.</p></div>}
          {section === "groups" && <div className="empty-state"><div className="empty-icon" aria-hidden="true">♧</div><h2>Group conversations</h2><p>Group membership, roles, and messages must be supplied by the owner server. This mainline backend has no public group routes yet; the browser cannot create or fake memberships.</p></div>}
          {section === "files" && <div className="empty-state"><div className="empty-icon" aria-hidden="true">▤</div><h2>Shared files</h2><p>Uploads and downloads require server-enforced attachment association, size policy, and authorization. File selection is disabled until these routes exist.</p></div>}
          {section === "settings" && <div className="settings-panel">
            <h2>Session & device security</h2><p className="muted">Signed in as {session.displayName || "Approved user"} · {session.role || "Member"} · {serverName}</p>
            <div className="settings-card"><h3>Current session</h3><p>Expires: {new Date(session.expiresUtc).toLocaleString()}</p><p>Time remaining: {remaining} minute(s)</p><p>Device: {device?.deviceName || session.deviceName || "Browser device"}</p><p>Platform: {device?.platform || "Browser"}</p>
              <p className="muted">Credentials live only in this tab's memory. Reloading requires a new owner-approved enrollment. This browser does not implement native certificate pinning.</p></div>
            <div className="settings-card"><h3>Active sessions</h3>{sessions.map(item =>
              <div className="session-row" key={item.sessionId}><span>{item.current ? "This session" : "Another session"}<small>Expires {new Date(item.expiresUtc).toLocaleString()}</small></span><span>{item.revokedUtc ? "Revoked" : "Active"}</span></div>)}
              <div className="settings-actions"><button type="button" onClick={loadSecurity}>Refresh</button><button type="button" className="danger-quiet" onClick={revokeOthers}>Revoke other sessions</button></div>
              {securityError && <p className="inline-error" role="alert">{securityError}</p>}
            </div>
            <button type="button" className="danger-quiet" onClick={onLogout}>Log out and revoke this session</button>
          </div>}
        </section>
      </div>
      <nav className="mobile-nav" aria-label="Mobile navigation">{sections.map(([id, label]) => <button type="button" key={id}
        className={section === id ? "active" : ""} onClick={() => { setSection(id); if (id !== "chats") setActive(null); }}>{label}</button>)}</nav>
    </main>
  );
}
