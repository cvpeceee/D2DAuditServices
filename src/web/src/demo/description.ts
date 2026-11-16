import type { DemoWorld } from "./catalog";
import { company } from "./client";

type Description = { body: string; updatedAt: string; updatedBy: string };

const store = new WeakMap<DemoWorld, Description>();

function draft(data: DemoWorld) {
  const passing = data.controls.filter((item) => item.status === "passing").length;
  const frameworks = data.frameworks.map((item) => item.name).join(", ");
  return [
    `${company.name} prepares this description for its SOC 2 examination. The company operates in ${company.region}. ${company.summary}`,
    `The system is the service ${company.name} provides to customers, together with the people, vendors, and connected systems that support it. Connected services: AWS, Azure, GCP, GitHub.`,
    `${data.people.length} people and ${data.vendors.length} vendors are on record. ${passing} of ${data.controls.length} controls are passing. Frameworks in the workspace: ${frameworks}.`,
    "This draft is generated from the workspace. An editor replaces any sentence that does not match how the system actually runs before the description goes into the audit package.",
  ].join("\n\n");
}

export function descriptionOf(data: DemoWorld) {
  const existing = store.get(data);
  if (existing) return existing;
  const row = {
    body: draft(data),
    updatedAt: new Date().toISOString(),
    updatedBy: "Draft",
  };
  store.set(data, row);
  return row;
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  });
}

function canWrite(actor: { role: string }) {
  return actor.role === "Admin" || actor.role === "ControlOwner" || actor.role === "Editor";
}

export function handleDescription(
  method: string,
  path: string,
  body: Record<string, unknown>,
  data: DemoWorld,
  actor: { name: string; role: string },
) {
  if (path === "/api/v1/system-description" && method === "GET") return json(descriptionOf(data));
  if (path === "/api/v1/system-description/redraft" && method === "POST") {
    if (!canWrite(actor)) return json({ code: "forbidden" }, 403);
    const row = descriptionOf(data);
    row.body = draft(data);
    row.updatedAt = new Date().toISOString();
    row.updatedBy = actor.name;
    return json(row);
  }
  if (path === "/api/v1/system-description" && method === "POST") {
    if (!canWrite(actor)) return json({ code: "forbidden" }, 403);
    const text = String(body.body ?? "").trim();
    if (!text) return json({ code: "invalid_description" }, 400);
    const row = descriptionOf(data);
    row.body = text;
    row.updatedAt = new Date().toISOString();
    row.updatedBy = actor.name;
    return json(row);
  }
  return null;
}
