import React, { useCallback, useEffect, useRef, useState } from "react";
import { attachmentsApi } from "./api/attachments.js";
import { groupsApi } from "./api/groups.js";
import { api, ApiError, explainApiError } from "./api/client.js";
import "./FileWorkspace.css";

const mergeFiles = (before, incoming) => {
  const byId = new Map(before.map(item => [item.attachmentId, item]));
  for (const item of incoming || []) if (item?.attachmentId) byId.set(item.attachmentId, item);
  return [...byId.values()].sort((a, b) =>
    (Date.parse(b.createdUtc) - Date.parse(a.createdUtc)) ||
    String(b.attachmentId).localeCompare(String(a.attachmentId)));
};
const safeDownloadName = name =>
  String(name || "attachment").replace(/[\\/\r\n\x00-\x1f]/g, "_").slice(0, 120) || "attachment";
const sizeLabel = bytes => Number.isFinite(Number(bytes))
  ? (Number(bytes) / 1024).toFixed(1) + " KiB" : "Unknown size";

export default function FileWorkspace({ session, onSessionLost }) {
  const token = session.bearerToken;
  const [groups, setGroups] = useState([]);
  const [directChats, setDirectChats] = useState([]);
  const [selectedScope, setSelectedScope] = useState({ type: "GROUP", id: null });
  const scopeType = selectedScope.type;
  const groupId = selectedScope.id;
  const [files, setFiles] = useState([]);
  const [cursor, setCursor] = useState(null);
  const [hasMore, setHasMore] = useState(false);
  const [selectedFile, setSelectedFile] = useState(null);
  const [busy, setBusy] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [downloading, setDownloading] = useState(null);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [refresh, setRefresh] = useState(0);
  const groupRef = useRef(null);
  const fileInputRef = useRef(null);
  groupRef.current = scopeType + ":" + groupId;

  const report = useCallback(error => {
    if (error?.status === 401) {
      onSessionLost("The owner server ended your session.");
      return;
    }
    if (error?.status === 403) {
      // The old scope's metadata is no longer authorized to remain visible.
      setFiles([]);
      setCursor(null);
      setHasMore(false);
      setSelectedFile(null);
      if (fileInputRef.current) fileInputRef.current.value = "";
    }
    setError(explainApiError(error));
  }, [onSessionLost]);

  useEffect(() => {
    const controller = new AbortController();
    Promise.all([
      groupsApi.list(token, controller.signal),
      api.inbox(token, controller.signal)
    ])
      .then(([groupData, directData]) => {
        if (controller.signal.aborted) return;
        const list = Array.isArray(groupData) ? groupData : [];
        const direct = Array.isArray(directData) ? directData : [];
        setGroups(list);
        setDirectChats(direct);
        setSelectedScope(previous => {
          const valid = previous.type === "GROUP"
            ? list.some(g => g.groupId === previous.id)
            : direct.some(c => c.conversationId === previous.id);
          if (valid) return previous;
          return list[0] ? { type: "GROUP", id: list[0].groupId }
            : direct[0] ? { type: "DIRECT", id: direct[0].conversationId }
            : { type: "GROUP", id: null };
        });
      })
      .catch(error => { if (!controller.signal.aborted) report(error); });
    return () => controller.abort();
  }, [token, refresh, report]);

  useEffect(() => {
    setFiles([]);
    setCursor(null);
    setHasMore(false);
    // A previous group's success/error must not appear as this scope's result.
    setNotice("");
    setError("");
    // A file selected in one scope cannot silently carry into another.
    setSelectedFile(null);
    if (fileInputRef.current) fileInputRef.current.value = "";
    if (!groupId) return;
    const controller = new AbortController();
    setBusy(true);
    attachmentsApi.list(token, scopeType, groupId, null, controller.signal)
      .then(page => {
        if (controller.signal.aborted) return;
        setFiles(mergeFiles([], page?.items));
        setCursor(page?.nextBeforeAttachmentId || null);
        setHasMore(Boolean(page?.hasMore));
        setError("");
      })
      .catch(error => { if (!controller.signal.aborted) report(error); })
      .finally(() => { if (!controller.signal.aborted) setBusy(false); });
    return () => controller.abort();
  }, [token, scopeType, groupId, refresh, report]);

  async function loadMore() {
    const id = groupId;
    if (!id || !cursor || !hasMore || busy) return;
    setBusy(true);
    try {
      const page = await attachmentsApi.list(token, scopeType, id, cursor);
      if (groupRef.current !== scopeType + ":" + id) return;
      setFiles(previous => mergeFiles(previous, page?.items));
      setCursor(page?.nextBeforeAttachmentId || null);
      setHasMore(Boolean(page?.hasMore));
    } catch (failure) { if (groupRef.current === scopeType + ":" + id) report(failure); }
    finally { if (groupRef.current === scopeType + ":" + id) setBusy(false); }
  }

  async function upload(event) {
    event.preventDefault();
    const id = groupId, file = selectedFile;
    if (!id || !file || uploading) return;
    setUploading(true);
    setError("");
    setNotice("");
    try {
      const saved = await attachmentsApi.upload(token, scopeType, id, file);
      if (groupRef.current !== scopeType + ":" + id) return;
      setFiles(previous => mergeFiles(previous, [saved]));
      setSelectedFile(null);
      if (fileInputRef.current) fileInputRef.current.value = "";
      setNotice("Saved to the owner server. Available to current conversation members.");
    } catch (failure) { if (groupRef.current === scopeType + ":" + id) report(failure); }
    finally {
      // This is component operation state, not scope-specific data. Switching
      // conversations while the request is in flight must never wedge uploads.
      setUploading(false);
    }
  }

  async function download(file) {
    if (downloading) return;
    const originalScope = scopeType + ":" + groupId;
    setDownloading(file.attachmentId);
    setError("");
    try {
      const content = await attachmentsApi.download(token, file.attachmentId);
      if (groupRef.current !== originalScope) return;
      // Downloaded bytes must match the owner server's authorized, durable
      // digest before being exposed to the user's filesystem.
      if (!globalThis.crypto?.subtle ||
          !/^[0-9a-f]{64}$/i.test(file.sha256 || "")) {
        throw new ApiError(422, "Attachment integrity verification is unavailable.");
      }
      const digest = await crypto.subtle.digest("SHA-256", await content.arrayBuffer());
      const actual = Array.from(new Uint8Array(digest),
        byte => byte.toString(16).padStart(2, "0")).join("");
      if (actual.toLowerCase() !== file.sha256.toLowerCase()) {
        throw new ApiError(422, "Attachment integrity check failed. Download refused.");
      }
      // Digest work can finish after a conversation switch. Never surface an
      // older scope's private file under the new selection.
      if (groupRef.current !== originalScope) return;
      const url = URL.createObjectURL(content);
      try {
        const anchor = document.createElement("a");
        anchor.href = url;
        anchor.download = safeDownloadName(file.originalFilename);
        anchor.rel = "noopener";
        anchor.style.display = "none";
        document.body.appendChild(anchor);
        anchor.click();
        anchor.remove();
      } finally {
        setTimeout(() => URL.revokeObjectURL(url), 5000);
      }
    } catch (failure) {
      if (groupRef.current === originalScope) report(failure);
    }
    finally { setDownloading(null); }
  }

  return <section className="files-workspace" aria-label="Shared conversation files">
    <header className="files-header">
      <div><span className="form-kicker">PRIVATE LAN / AUTHORIZED ATTACHMENTS</span>
        <h2>Shared files</h2>
        <p>Only current group or direct-conversation members can access stored files.</p>
      </div>
      <button type="button" className="quiet-button" onClick={() => setRefresh(value => value + 1)}>Refresh</button>
    </header>
    {error && <p role="alert" className="inline-error">{error}</p>}
    {notice && <p role="status" className="inline-notice">{notice}</p>}
    <div className="files-controls">
      <label>Conversation
        <select value={groupId ? scopeType + ":" + groupId : ""}
          onChange={event => {
            const [kind, id] = event.target.value.split(":");
            if ((kind === "GROUP" && groups.some(g => g.groupId === id)) ||
                (kind === "DIRECT" && directChats.some(c => c.conversationId === id))) {
              setSelectedScope({ type: kind, id });
            }
          }} disabled={!groups.length && !directChats.length}>
          {!groups.length && !directChats.length && <option value="">No authorized conversations</option>}
          <optgroup label="Groups">
            {groups.map(group => <option value={"GROUP:" + group.groupId} key={group.groupId}>{group.name}</option>)}
          </optgroup>
          <optgroup label="Direct conversations">
            {directChats.map(chat => <option value={"DIRECT:" + chat.conversationId} key={chat.conversationId}>
              {chat.otherUser?.displayName || chat.otherUser?.username || "Direct conversation"}
            </option>)}
          </optgroup>
        </select>
      </label>
      <form onSubmit={upload} className="files-upload-form">
        <label>Upload file (maximum 10 MiB)
          <input ref={fileInputRef} type="file" disabled={!groupId || uploading}
            onChange={event => setSelectedFile(event.target.files?.[0] || null)} />
        </label>
        <button type="submit" disabled={!groupId || !selectedFile || uploading}>
          {uploading ? "Uploading…" : "Upload to group"}
        </button>
      </form>
    </div>
    {!groups.length && !directChats.length &&
      <p className="empty-copy">Create a group or open a direct conversation to share files.</p>}
    {busy && <p className="muted" role="status">Reading authorized file records…</p>}
    {!busy && groupId && !files.length && <p className="muted">No uploaded files in this conversation.</p>}
    <div className="files-records">
      {files.map(file => <article className="file-record" key={file.attachmentId}>
        <div className="file-details">
          <strong>{file.originalFilename}</strong>
          <span>{sizeLabel(file.byteSize)} · {new Date(file.createdUtc).toLocaleString()}</span>
          <small>SHA-256: {file.sha256}</small>
        </div>
        <button type="button" disabled={Boolean(downloading)}
          onClick={() => download(file)}>
          {downloading === file.attachmentId ? "Downloading…" : "Download"}
        </button>
      </article>)}
    </div>
    {hasMore && <button type="button" className="quiet-button" disabled={busy} onClick={loadMore}>
      {busy ? "Loading…" : "Load older files"}
    </button>}
  </section>;
}
