import React, { useCallback, useEffect, useRef, useState } from "react";
import { attachmentsApi } from "./api/attachments.js";
import { groupsApi } from "./api/groups.js";
import { explainApiError } from "./api/client.js";
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
  const [groupId, setGroupId] = useState(null);
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
  groupRef.current = groupId;

  const report = useCallback(error => {
    if (error?.status === 401) onSessionLost("The owner server ended your session.");
    else setError(explainApiError(error));
  }, [onSessionLost]);

  useEffect(() => {
    const controller = new AbortController();
    groupsApi.list(token, controller.signal)
      .then(data => {
        if (controller.signal.aborted) return;
        const list = Array.isArray(data) ? data : [];
        setGroups(list);
        setGroupId(previous => list.some(g => g.groupId === previous)
          ? previous : list[0]?.groupId || null);
      })
      .catch(error => { if (!controller.signal.aborted) report(error); });
    return () => controller.abort();
  }, [token, refresh, report]);

  useEffect(() => {
    setFiles([]);
    setCursor(null);
    setHasMore(false);
    if (!groupId) return;
    const controller = new AbortController();
    setBusy(true);
    attachmentsApi.list(token, "GROUP", groupId, null, controller.signal)
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
  }, [token, groupId, refresh, report]);

  async function loadMore() {
    const id = groupId;
    if (!id || !cursor || !hasMore || busy) return;
    setBusy(true);
    try {
      const page = await attachmentsApi.list(token, "GROUP", id, cursor);
      if (groupRef.current !== id) return;
      setFiles(previous => mergeFiles(previous, page?.items));
      setCursor(page?.nextBeforeAttachmentId || null);
      setHasMore(Boolean(page?.hasMore));
    } catch (failure) { if (groupRef.current === id) report(failure); }
    finally { if (groupRef.current === id) setBusy(false); }
  }

  async function upload(event) {
    event.preventDefault();
    const id = groupId, file = selectedFile;
    if (!id || !file || uploading) return;
    setUploading(true);
    setError("");
    setNotice("");
    try {
      const saved = await attachmentsApi.upload(token, "GROUP", id, file);
      if (groupRef.current !== id) return;
      setFiles(previous => mergeFiles(previous, [saved]));
      setSelectedFile(null);
      if (fileInputRef.current) fileInputRef.current.value = "";
      setNotice("Saved to the owner server. Available to current group members.");
    } catch (failure) { if (groupRef.current === id) report(failure); }
    finally { if (groupRef.current === id) setUploading(false); }
  }

  async function download(file) {
    if (downloading) return;
    setDownloading(file.attachmentId);
    setError("");
    try {
      const content = await attachmentsApi.download(token, file.attachmentId);
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
    } catch (failure) { report(failure); }
    finally { setDownloading(null); }
  }

  return <section className="files-workspace" aria-label="Shared group files">
    <header className="files-header">
      <div><span className="form-kicker">PRIVATE LAN / AUTHORIZED ATTACHMENTS</span>
        <h2>Shared group files</h2>
        <p>File metadata and bytes are served only to current members by the owner server.</p>
      </div>
      <button type="button" className="quiet-button" onClick={() => setRefresh(value => value + 1)}>Refresh</button>
    </header>
    {error && <p role="alert" className="inline-error">{error}</p>}
    {notice && <p role="status" className="inline-notice">{notice}</p>}
    <div className="files-controls">
      <label>Group
        <select value={groupId || ""} onChange={event => setGroupId(event.target.value || null)}
          disabled={!groups.length}>
          {groups.length === 0 && <option value="">No authorized groups</option>}
          {groups.map(group => <option value={group.groupId} key={group.groupId}>{group.name}</option>)}
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
    {!groups.length && <p className="empty-copy">Join or create a group to share files.</p>}
    {busy && <p className="muted" role="status">Reading authorized file records…</p>}
    {!busy && groupId && !files.length && <p className="muted">No uploaded files in this group.</p>}
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
