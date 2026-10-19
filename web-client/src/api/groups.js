import { requestJson } from "./client.js";

function validId(id) {
  if (typeof id !== "string" || !/^[\da-f]{8}-[\da-f]{4}-[\da-f]{4}-[\da-f]{4}-[\da-f]{12}$/i.test(id)) {
    throw new TypeError("Invalid group or user ID.");
  }
  return id;
}
const groupPath = (groupId) => "/api/v1/groups/" + validId(groupId);

export const groupsApi = {
  list: (token, signal) => requestJson("/api/v1/groups", { token, signal }),
  create: (token, { name, topic }) => requestJson("/api/v1/groups", {
    token, method: "POST", body: { name: name.trim(), topic: topic.trim() || null }
  }),
  details: (token, groupId, signal) => requestJson(groupPath(groupId), { token, signal }),
  update: (token, groupId, { name, topic }) => requestJson(groupPath(groupId), {
    token, method: "PUT", body: { name: name.trim(), topic: topic.trim() || null }
  }),
  events: (token, groupId, signal) => requestJson(groupPath(groupId) + "/events?limit=100", { token, signal }),
  messages: (token, groupId, afterMessageId, signal) => requestJson(
    groupPath(groupId) + "/messages?limit=100" +
      (afterMessageId ? "&afterMessageId=" + validId(afterMessageId) : ""),
    { token, signal }
  ),
  sendMessage: (token, groupId, clientMessageId, body) => requestJson(
    groupPath(groupId) + "/messages",
    { method: "POST", token, body: { clientMessageId: validId(clientMessageId), body, replyToMessageId: null } }
  ),
  addMember: (token, groupId, userId, role) => requestJson(groupPath(groupId) + "/members", {
    token, method: "POST", body: { userId: validId(userId), role }
  }),
  removeMember: (token, groupId, userId) => requestJson(groupPath(groupId) + "/members/" + validId(userId), {
    token, method: "DELETE"
  }),
  changeRole: (token, groupId, userId, role) => requestJson(groupPath(groupId) + "/members/" + validId(userId) + "/role", {
    token, method: "PUT", body: { role }
  })
};
