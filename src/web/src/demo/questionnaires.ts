import type { DemoWorld } from "./catalog";

type Answer = {
  id: string;
  position: number;
  question: string;
  suggestion: string;
  citation: string;
  answer: string;
  reviewerName: string;
  accepted: boolean;
};

type Sheet = {
  id: string;
  name: string;
  customerName: string;
  status: string;
  createdAt: string;
  questions: number;
  accepted: number;
  answers: Answer[];
};

const store = new WeakMap<DemoWorld, Sheet[]>();

const knowledge: [string, string][] = [
  ["Control CR-02 Encryption at rest", "Production data is encrypted at rest."],
  [
    "Control CM-11 Code changes are peer-reviewed",
    "Production code changes are reviewed by someone other than the author.",
  ],
  [
    "Policy POL-INC Incident response",
    "A person who sees a security incident reports it the same day.",
  ],
];

function words(value: string) {
  return new Set(
    value
      .toLowerCase()
      .split(/[^a-z0-9]+/)
      .filter((word) => word.length > 3),
  );
}

function suggest(question: string) {
  const left = words(question);
  let best: [string, string] | null = null;
  let score = 0;
  for (const item of knowledge) {
    const right = words(`${item[0]} ${item[1]}`);
    let next = 0;
    for (const word of left) if (right.has(word)) next += 1;
    if (next > score) {
      score = next;
      best = item;
    }
  }
  if (!best)
    return { suggestion: "No matching policy, control, or evidence was found.", citation: "" };
  const excerpt = best[1].slice(0, 240);
  return { suggestion: `From ${best[0]}: ${excerpt}`, citation: best[0] };
}

function answer(position: number, question: string, accepted: boolean, reviewer: string): Answer {
  const suggested = suggest(question);
  return {
    id: crypto.randomUUID(),
    position,
    question,
    suggestion: suggested.suggestion,
    citation: suggested.citation,
    answer: suggested.suggestion,
    reviewerName: accepted ? reviewer : "",
    accepted,
  };
}

function summarize(row: Sheet): Sheet {
  row.questions = row.answers.length;
  row.accepted = row.answers.filter((item) => item.accepted).length;
  row.status =
    row.answers.length > 0 && row.accepted === row.answers.length ? "Accepted" : "InReview";
  return row;
}

export function questionnairesOf(data: DemoWorld) {
  const existing = store.get(data);
  if (existing) return existing;
  const now = new Date();
  now.setUTCDate(now.getUTCDate() - 3);
  const row = summarize({
    id: crypto.randomUUID(),
    name: "Harborline security questionnaire",
    customerName: "Northgate Assurance",
    status: "InReview",
    createdAt: now.toISOString(),
    questions: 0,
    accepted: 0,
    answers: [
      answer(1, "Is production data encrypted at rest?", true, "Chandra Vell"),
      answer(2, "Are production changes reviewed before they ship?", false, ""),
      answer(3, "How does an employee report a security incident?", true, "Chandra Vell"),
    ],
  });
  const rows = [row];
  store.set(data, rows);
  return rows;
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

function questionsFrom(text: string) {
  const lines = text
    .replaceAll("\r\n", "\n")
    .split("\n")
    .map((line) => line.trim())
    .filter(Boolean);
  if (lines.length === 0) return [];
  const first = lines[0].split(",")[0]?.replaceAll('"', "").trim().toLowerCase();
  const start = first === "question" ? 1 : 0;
  return lines
    .slice(start)
    .map((line) => line.split(",")[0]?.replaceAll('"', "").trim() ?? "")
    .filter((line) => line.length > 0)
    .slice(0, 200);
}

function csvCell(value: string) {
  const safe = /^[=+\-@\t\r]/.test(value) ? `'${value}` : value;
  if (safe.includes(",") || safe.includes('"') || safe.includes("\n") || safe.includes("\r")) {
    return `"${safe.replaceAll('"', '""')}"`;
  }
  return safe;
}

export function handleQuestionnaires(
  method: string,
  path: string,
  body: Record<string, unknown>,
  data: DemoWorld,
  actor: { name: string; role: string },
) {
  const rows = questionnairesOf(data);
  if (path === "/api/v1/questionnaires" && method === "GET") {
    return json(
      rows.map((row) => ({
        id: row.id,
        name: row.name,
        customerName: row.customerName,
        status: row.status,
        createdAt: row.createdAt,
        questions: row.questions,
        accepted: row.accepted,
      })),
    );
  }
  if (path === "/api/v1/questionnaires" && method === "POST") {
    if (!canWrite(actor)) return json({ code: "forbidden" }, 403);
    const name = String(body.name ?? "").trim();
    const customerName = String(body.customerName ?? "").trim();
    const fileName = String(body.fileName ?? "").trim();
    let text = "";
    try {
      text = atob(String(body.contentBase64 ?? ""));
    } catch {
      return json({ code: "invalid_questionnaire" }, 400);
    }
    const questions = fileName.toLowerCase().endsWith(".xlsx") ? [] : questionsFrom(text);
    if (!name || !customerName || questions.length === 0) {
      return json({ code: "invalid_questionnaire" }, 400);
    }
    const row = summarize({
      id: crypto.randomUUID(),
      name,
      customerName,
      status: "InReview",
      createdAt: new Date().toISOString(),
      questions: 0,
      accepted: 0,
      answers: questions.map((question, index) => answer(index + 1, question, false, "")),
    });
    rows.unshift(row);
    return json(row);
  }
  const exportMatch = /^\/api\/v1\/questionnaires\/([^/]+)\/export$/.exec(path);
  if (exportMatch && method === "GET") {
    const row = rows.find((item) => item.id === exportMatch[1]);
    if (!row) return json({ code: "not_found" }, 404);
    const lines = ["position,question,suggestion,citation,answer,accepted"];
    for (const item of row.answers) {
      lines.push(
        [
          item.position,
          csvCell(item.question),
          csvCell(item.suggestion),
          csvCell(item.citation),
          csvCell(item.answer),
          item.accepted ? "yes" : "no",
        ].join(","),
      );
    }
    return new Response(lines.join("\n"), {
      headers: {
        "content-type": "text/csv",
        "content-disposition": `attachment; filename="questionnaire-${row.id}.csv"`,
      },
    });
  }
  const detailMatch = /^\/api\/v1\/questionnaires\/([^/]+)$/.exec(path);
  if (detailMatch && method === "GET") {
    const row = rows.find((item) => item.id === detailMatch[1]);
    return row ? json(row) : json({ code: "not_found" }, 404);
  }
  const reviewMatch = /^\/api\/v1\/questionnaires\/([^/]+)\/answers\/([^/]+)$/.exec(path);
  if (reviewMatch && method === "POST") {
    if (!canWrite(actor)) return json({ code: "forbidden" }, 403);
    const row = rows.find((item) => item.id === reviewMatch[1]);
    const item = row?.answers.find((entry) => entry.id === reviewMatch[2]);
    if (!row || !item) return json({ code: "not_found" }, 404);
    if (typeof body.answer === "string") item.answer = body.answer;
    if (typeof body.accepted === "boolean") {
      item.accepted = body.accepted;
      item.reviewerName = body.accepted ? actor.name : "";
    }
    summarize(row);
    return json(item);
  }
  return null;
}
