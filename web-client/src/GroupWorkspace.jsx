import React, { useCallback, useEffect, useRef, useState } from "react";
import { api, explainApiError } from "./api/client.js";
import { groupsApi } from "./api/groups.js";
import { secureId } from "./auth/enrollment.js";
import "./GroupWorkspace.css";

const displayName = person => person?.displayName || person?.username || "Unknown user";
const roleLabel = role => String(role || "MEMBER").toLowerCase();
const mergeGroupMessages = (oldMessages, newMessages) => {
  const byId = new Map(oldMessages.map(item => [item.messageId, item]));
  for (const item of newMessages || []) if (item?.messageId) byId.set(item.messageId, item);
  return [...byId.values()].sort((a, b) =>
    (Date.parse(a.createdUtc) - Date.parse(b.createdUtc)) || String(a.messageId).localeCompare(String(b.messageId)));
};

export default function GroupWorkspace({ session, onSessionLost, realtimeEvent }) {
  const token = session.bearerToken;
  const [groups, setGroups] = useState([]);
  const [selectedId, setSelectedId] = useState(null);
  const [details, setDetails] = useState(null);
  const [events, setEvents] = useState([]);
  const [reload, setReload] = useState(0);
  const [listBusy, setListBusy] = useState(true);
  const [detailBusy, setDetailBusy] = useState(false);
  const [busyAction, setBusyAction] = useState(false);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const [newName, setNewName] = useState("");
  const [newTopic, setNewTopic] = useState("");
  const [editName, setEditName] = useState("");
  const [editTopic, setEditTopic] = useState("");
  const [directoryQuery, setDirectoryQuery] = useState("");
  const [directoryUsers, setDirectoryUsers] = useState([]);
  const [directoryBusy, setDirectoryBusy] = useState(false);
  const [newRole, setNewRole] = useState("MEMBER");
  const [groupMessages, setGroupMessages] = useState([]);
  const [groupMessageDraft, setGroupMessageDraft] = useState("");
  const [messageCursor, setMessageCursor] = useState(null);
  const [hasNextMessages, setHasNextMessages] = useState(false);
  const [messageBusy, setMessageBusy] = useState(false);
  const [messageSendBusy, setMessageSendBusy] = useState(false);
  const activeGroupRef = useRef(null);
  const pendingGroupSendRef = useRef(null);
  activeGroupRef.current = selectedId;

  const report = useCallback(error => {
    // A 403 can mean the user lost this group's membership while their
    // authenticated session remains valid. Keep it local to this workspace.
    if (error?.status === 401) {
      onSessionLost("Your browser session is no longer authorized.");
    } else {
      // Group-scoped 403 revokes access to this conversation, not the session.
      if (error?.status === 403) { setGroupMessages([]); setDetails(null); }
      setError(explainApiError(error));
    }
  }, [onSessionLost]);

  useEffect(() => {
    const controller = new AbortController();
    setListBusy(true);
    groupsApi.list(token, controller.signal)
      .then(data => {
        if (controller.signal.aborted) return;
        const list = Array.isArray(data) ? data : [];
        setGroups(list);
        setSelectedId(previous => list.some(group => group.groupId === previous)
          ? previous : list[0]?.groupId || null);
      })
      .catch(error => { if (!controller.signal.aborted) report(error); })
      .finally(() => { if (!controller.signal.aborted) setListBusy(false); });
    return () => controller.abort();
  }, [token, reload, report]);

  useEffect(() => {
    if (!selectedId) { setDetails(null); setEvents([]); return; }
    const controller = new AbortController();
    setDetailBusy(true);
    setError("");
    Promise.all([groupsApi.details(token, selectedId, controller.signal), groupsApi.events(token, selectedId, controller.signal)])
      .then(([group, audit]) => {
        if (controller.signal.aborted) return;
        setDetails(group);
        setEditName(group.name || "");
        setEditTopic(group.topic || "");
        setEvents(Array.isArray(audit) ? audit : []);
      })
      .catch(error => { if (!controller.signal.aborted) { setDetails(null); report(error); } })
      .finally(() => { if (!controller.signal.aborted) setDetailBusy(false); });
    return () => controller.abort();
  }, [token, selectedId, reload, report]);

  useEffect(() => {
    // An old group's page request may settle after selection changes; its
    // guarded finalizer must not leave this group's pager permanently busy.
    setMessageBusy(false);
    setGroupMessages([]);
    setMessageCursor(null);
    setHasNextMessages(false);
    setGroupMessageDraft("");
    pendingGroupSendRef.current = null;
    if (!selectedId) return;
    const controller = new AbortController();
    setMessageBusy(true);
    groupsApi.messages(token, selectedId, null, controller.signal)
      .then(page => {
        if (controller.signal.aborted) return;
        // Realtime may deliver a newer message while the initial GET is in
        // flight; merging into current state prevents the GET from erasing it.
        setGroupMessages(previous => mergeGroupMessages(previous, page?.items));
        setMessageCursor(page?.nextAfterMessageId || null);
        setHasNextMessages(Boolean(page?.hasMore));
      })
      .catch(error => { if (!controller.signal.aborted) report(error); })
      .finally(() => { if (!controller.signal.aborted) setMessageBusy(false); });
    return () => controller.abort();
  }, [token, selectedId, reload, report]);

  async function loadMoreMessages() {
    const id = selectedId;
    if (!id || !messageCursor || !hasNextMessages || messageBusy) return;
    setMessageBusy(true);
    try {
      const page = await groupsApi.messages(token, id, messageCursor);
      if (activeGroupRef.current !== id) return;
      setGroupMessages(previous => mergeGroupMessages(previous, page?.items));
      setMessageCursor(page?.nextAfterMessageId || null);
      setHasNextMessages(Boolean(page?.hasMore));
    } catch (error) { if (activeGroupRef.current === id) report(error); }
    finally { if (activeGroupRef.current === id) setMessageBusy(false); }
  }

  async function sendGroupMessage(event) {
    event.preventDefault();
    const id = selectedId;
    const body = groupMessageDraft.trim();
    if (!id || !body || messageSendBusy) return;
    setMessageSendBusy(true);
    setError("");
    try {
      // Keep a stable server idempotency key if the POST commits but its reply
      // disappears. A retry of the exact same group/body cannot duplicate it.
      const pending = pendingGroupSendRef.current;
      const clientMessageId = pending?.groupId === id && pending.body === body
        ? pending.clientMessageId : secureId();
      pendingGroupSendRef.current = { groupId: id, body, clientMessageId };
      const result = await groupsApi.sendMessage(token, id, clientMessageId, body);
      pendingGroupSendRef.current = null;
      if (activeGroupRef.current !== id) return;
      setGroupMessages(previous => mergeGroupMessages(previous, result?.messageId ? [result] : []));
      setGroupMessageDraft("");
    } catch (error) { if (activeGroupRef.current === id) report(error); }
    finally {
      // This operation flag is global to the composer, not the active group.
      setMessageSendBusy(false);
    }
  }

  useEffect(() => {
    if (!realtimeEvent) return;
    const { event, payload } = realtimeEvent;
    if (event === "GroupMembershipRevoked") {
      if (payload?.groupId === activeGroupRef.current) {
        setGroupMessages([]);
        setDetails(null);
        setSelectedId(null);
        setError("Your membership in this group was removed by the owner server.");
      }
      setReload(value => value + 1);
      return;
    }
    if (event === "GroupMessageCreated") {
      // SignalR payloads are hints for the current UI; durable history and
      // current membership authority always come from the owner server.
      if (payload?.scopeType === "GROUP" && payload?.scopeId === activeGroupRef.current) {
        setGroupMessages(previous => mergeGroupMessages(previous, [payload]));
      }
      return;
    }
    if (event !== "GroupReconnect") return;

    const id = activeGroupRef.current;
    if (!id) return;
    let cancelled = false;
    const latestId = groupMessages.at(-1)?.messageId || null;
    (async () => {
      let cursor = latestId;
      try {
        for (let pageNumber = 0; pageNumber < 12; pageNumber++) {
          const page = await groupsApi.messages(token, id, cursor);
          if (cancelled || activeGroupRef.current !== id) return;
          setGroupMessages(previous => mergeGroupMessages(previous, page?.items));
          if (!page?.hasMore || !page?.nextAfterMessageId ||
              cursor === page.nextAfterMessageId) break;
          cursor = page.nextAfterMessageId;
        }
      } catch (error) {
        if (!cancelled && activeGroupRef.current === id) report(error);
      }
    })();
    return () => { cancelled = true; };
  }, [realtimeEvent, token]);

  useEffect(() => {
    // A previous directory fetch may have been aborted when the input was
    // erased or the selected group changed. Reset its obsolete busy state.
    setDirectoryBusy(false);
    if (!directoryQuery.trim() || !details) { setDirectoryUsers([]); return; }
    const controller = new AbortController();
    const timer = setTimeout(async () => {
      setDirectoryBusy(true);
      try {
        const found = await api.users(token, directoryQuery.trim(), controller.signal);
        if (!controller.signal.aborted) setDirectoryUsers(Array.isArray(found?.users) ? found.users : []);
      } catch (error) {
        if (!controller.signal.aborted) report(error);
      } finally {
        if (!controller.signal.aborted) setDirectoryBusy(false);
      }
    }, 300);
    return () => { clearTimeout(timer); controller.abort(); };
  }, [directoryQuery, details?.groupId, token, report]);

  function refreshed(note) {
    setError("");
    setMessage(note);
    setReload(count => count + 1);
  }

  async function mutate(action, success) {
    if (busyAction) return false;
    setBusyAction(true);
    setError("");
    setMessage("");
    try {
      await action();
      refreshed(success);
      return true;
    } catch (error) { report(error); return false; }
    finally { setBusyAction(false); }
  }

  async function createGroup(event) {
    event.preventDefault();
    if (!newName.trim()) return;
    await mutate(async () => {
      const saved = await groupsApi.create(token, { name: newName, topic: newTopic });
      setSelectedId(saved.groupId);
      setNewName("");
      setNewTopic("");
    }, "Group created on the owner server.");
  }

  async function saveGroup(event) {
    event.preventDefault();
    if (!details || !editName.trim()) return;
    await mutate(() => groupsApi.update(token, details.groupId, { name: editName, topic: editTopic }), "Group details updated.");
  }

  async function addPerson(user) {
    if (!details || !user?.userId) return;
    const applied = await mutate(
      () => groupsApi.addMember(token, details.groupId, user.userId, isOwner ? newRole : "MEMBER"),
      "Group member added."
    );
    if (applied) {
      setDirectoryQuery("");
      setDirectoryUsers([]);
    }
  }

  async function removePerson(member) {
    if (!details || !member?.userId || member.role === "OWNER") return;
    const isSelf = member.userId === session.userId;
    if (!window.confirm(isSelf ? "Leave this group? You will lose its membership." : "Remove " + displayName(member) + " from this group?")) return;
    const currentId = details.groupId;
    await mutate(async () => {
      await groupsApi.removeMember(token, currentId, member.userId);
      if (isSelf) setSelectedId(null);
    }, isSelf ? "You left the group." : "Member removed from group.");
  }

  async function changeRole(member, role) {
    if (!details || member.role === "OWNER" || role === member.role) return;
    if (!window.confirm("Change " + displayName(member) + " to " + role.toLowerCase() + "?")) return;
    await mutate(() => groupsApi.changeRole(token, details.groupId, member.userId, role), "Member role updated.");
  }

  const isOwner = details?.myRole === "OWNER";
  const isAdmin = details?.myRole === "ADMIN";
  const canModerate = isOwner || isAdmin;
  const potentialMembers = directoryUsers.filter(user =>
    user.userId !== session.userId && !details?.members?.some(member => member.userId === user.userId));

  return <div className="groups-workspace">
    <header className="groups-heading">
      <div><span className="form-kicker">P3 / GROUP DIRECTORY</span><h2>Group workspace</h2>
        <p>Membership and roles are enforced by the owner server.</p></div>
      <button type="button" className="quiet-button" onClick={() => setReload(value => value + 1)}>Refresh</button>
    </header>

    {error && <p className="inline-error" role="alert">{error}</p>}
    {message && <p className="inline-notice" role="status">{message}</p>}

    <div className="groups-grid">
      <aside className="groups-list" aria-label="My groups">
        <form className="group-create" onSubmit={createGroup}>
          <h3>Create a group</h3>
          <label>Group name<input value={newName} required maxLength={120} onChange={event => setNewName(event.target.value)} placeholder="Example: Engineering" /></label>
          <label>Topic (optional)<textarea value={newTopic} maxLength={500} rows={2} onChange={event => setNewTopic(event.target.value)} placeholder="What is this group for?" /></label>
          <button type="submit" disabled={busyAction || !newName.trim()}>Create on server</button>
        </form>
        <h3>My groups {listBusy ? "…" : "(" + groups.length + ")"}</h3>
        {listBusy && <p className="muted">Loading memberships…</p>}
        {!listBusy && groups.length === 0 && <p className="empty-copy">No groups yet. Create one to invite existing users.</p>}
        <div className="group-buttons">{groups.map(group =>
          <button key={group.groupId} type="button" className={"group-picker " + (selectedId === group.groupId ? "selected" : "")}
            onClick={() => setSelectedId(group.groupId)}>
            <span className="group-picker-symbol" aria-hidden="true">#</span>
            <span><strong>{group.name}</strong><small>{roleLabel(group.myRole)} · {group.topic || "No topic"}</small></span>
          </button>)}</div>
      </aside>

      <section className="group-details" aria-label="Selected group">
        {!selectedId && <div className="group-placeholder"><h3>Select or create a group</h3><p>Only groups with current server-authorized membership appear here.</p></div>}
        {selectedId && detailBusy && <p className="muted">Loading group details and activity…</p>}
        {selectedId && !detailBusy && details && <>
          <div className="group-overview">
            <h3>{details.name}</h3><p>{details.topic || "No topic set"}</p>
            <span className="group-role">Your role: {roleLabel(details.myRole)}</span>
          </div>
          {canModerate && <form className="group-edit" onSubmit={saveGroup}>
            <h3>Group details</h3>
            <label>Name<input required value={editName} maxLength={120} onChange={event => setEditName(event.target.value)} /></label>
            <label>Topic<textarea value={editTopic} rows={2} maxLength={500} onChange={event => setEditTopic(event.target.value)} /></label>
            <button type="submit" disabled={busyAction || !editName.trim()}>Save group details</button>
          </form>}

          <div className="group-members">
            <h3>Members ({details.members?.length || 0})</h3>
            {(details.members || []).map(member => {
              const self = member.userId === session.userId;
              const canRemove = member.role !== "OWNER" &&
                (self || isOwner || isAdmin && member.role === "MEMBER");
              return <div key={member.userId} className="group-member">
                <span className="avatar" aria-hidden="true">{displayName(member).slice(0, 1).toUpperCase()}</span>
                <span className="member-name"><strong>{displayName(member)}{self ? " (you)" : ""}</strong><small>@{member.username} · {roleLabel(member.role)}</small></span>
                {isOwner && member.role !== "OWNER" && <label className="group-role-select">
                  <span className="sr-only">Change role for {displayName(member)}</span>
                  <select aria-label={"Role for " + displayName(member)} disabled={busyAction} value={member.role}
                    onChange={event => changeRole(member, event.target.value)}>
                    <option value="MEMBER">Member</option><option value="ADMIN">Admin</option>
                  </select>
                </label>}
                {canRemove && <button type="button" className="danger-quiet" disabled={busyAction}
                  onClick={() => removePerson(member)}>{self ? "Leave" : "Remove"}</button>}
              </div>;
            })}
          </div>

          {canModerate && <div className="group-add">
            <h3>Add an existing user</h3>
            <label>Search directory<input type="search" autoComplete="off" value={directoryQuery}
              onChange={event => setDirectoryQuery(event.target.value)} placeholder="Search by username or name" /></label>
            {isOwner && <label>Assigned group role<select value={newRole} onChange={event => setNewRole(event.target.value)}>
              <option value="MEMBER">Member</option><option value="ADMIN">Admin</option></select></label>}
            {directoryBusy && <p className="muted">Searching real user directory…</p>}
            {potentialMembers.map(user => <div className="group-member" key={user.userId}>
              <span className="member-name"><strong>{displayName(user)}</strong><small>@{user.username}</small></span>
              <button type="button" disabled={busyAction} onClick={() => addPerson(user)}>Add</button>
            </div>)}
            {directoryQuery && !directoryBusy && !potentialMembers.length && <p className="muted">No eligible users match.</p>}
          </div>}

          <div className="group-events">
            <h3>Group activity (first 100 events)</h3>
            {events.length === 0 && <p className="muted">No events in the current page.</p>}
            {events.map(event => <div className="group-event" key={event.groupEventId}>
              <strong>{String(event.eventType || "Group event").replaceAll("_", " ").toLowerCase()}</strong>
              <time dateTime={event.createdUtc}>{new Date(event.createdUtc).toLocaleString()}</time>
            </div>)}
          </div>
          <section className="group-message-boundary" aria-label="Group conversation">
            <h3>Group conversation</h3>
            <p>Messages are stored by the owner server and only readable by current members.</p>
            <div className="group-message-log" role="log" aria-label={"Messages in " + details.name} aria-live="polite">
              {messageBusy && !groupMessages.length && <p className="muted">Loading authorized messages…</p>}
              {!messageBusy && !groupMessages.length && <p className="muted">No group messages yet.</p>}
              {groupMessages.map(item => {
                const sender = details.members?.find(member => member.userId === item.senderUserId);
                return <article className="group-message-entry" key={item.messageId}>
                  <div className="group-message-byline">
                    <strong>{item.senderUserId === session.userId ? "You" : sender ? displayName(sender) : "Group member"}</strong>
                    <time dateTime={item.createdUtc}>{new Date(item.createdUtc).toLocaleString()}</time>
                  </div>
                  <p>{item.deletedUtc ? "Message deleted" : item.body}</p>
                </article>;
              })}
            </div>
            {hasNextMessages && <button type="button" className="quiet-button"
              disabled={messageBusy} onClick={loadMoreMessages}>
              {messageBusy ? "Loading…" : "Load next messages"}
            </button>}
            <form className="group-message-form" onSubmit={sendGroupMessage}>
              <label>Message this group
                <textarea value={groupMessageDraft} rows={3} maxLength={4000}
                  onChange={event => {
                    const next = event.target.value;
                    if (pendingGroupSendRef.current?.body !== next.trim())
                      pendingGroupSendRef.current = null;
                    setGroupMessageDraft(next);
                  }}
                  placeholder="Write to current group members" required />
              </label>
              <button type="submit" disabled={messageSendBusy || !groupMessageDraft.trim()}>
                {messageSendBusy ? "Sending…" : "Send to group"}
              </button>
            </form>
          </section>
        </>}
      </section>
    </div>
  </div>;
}
