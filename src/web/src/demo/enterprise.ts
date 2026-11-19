import { company, type DemoNode, type DemoWorld } from "./catalog";
import { trustStateOf } from "./m6";
import { plainPdf } from "./pdf";

type Actor = { email: string; name: string; role: string };

type DemoApiKey = {
  id: string;
  name: string;
  prefix: string;
  scopes: string;
  createdByEmail: string;
  createdAt: string;
  revokedAt: string | null;
};

type DemoWebhook = {
  id: string;
  name: string;
  url: string;
  events: string;
  enabled: boolean;
  createdAt: string;
};

type EnterpriseState = {
  keys: DemoApiKey[];
  webhooks: DemoWebhook[];
};

const stateByWorld = new WeakMap<DemoWorld, EnterpriseState>();

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function canWrite(actor: Actor) {
  return actor.role === "Admin" || actor.role === "ControlOwner" || actor.role === "Editor";
}

function getState(world: DemoWorld): EnterpriseState {
  let state = stateByWorld.get(world);
  if (!state) {
    state = {
      keys: [
        {
          id: "key-demo-1",
          name: "CI Compliance Export",
          prefix: "d2d_api_harbor01",
          scopes: "read:controls,read:evidence,read:security",
          createdByEmail: `chandra.vell@${company.domain}`,
          createdAt: new Date(Date.now() - 14 * 86_400_000).toISOString(),
          revokedAt: null,
        },
      ],
      webhooks: [
        {
          id: "wh-demo-1",
          name: "Security SIEM Stream",
          url: `https://siem.${company.domain}/hooks/d2d`,
          events: "test.failed,exposure.critical,trust.request",
          enabled: true,
          createdAt: new Date(Date.now() - 10 * 86_400_000).toISOString(),
        },
      ],
    };
    stateByWorld.set(world, state);
  }
  return state;
}

async function sha256Hex(value: string): Promise<string> {
  const bytes = new TextEncoder().encode(value);
  const digest = await crypto.subtle.digest("SHA-256", bytes);
  return [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, "0")).join("");
}

function leavesOf(nodes: DemoNode[]): DemoNode[] {
  const result: DemoNode[] = [];
  for (const node of nodes) {
    if (node.children.length === 0) {
      result.push(node);
    } else {
      result.push(...leavesOf(node.children));
    }
  }
  return result;
}

function simulateFramework(world: DemoWorld, key: string) {
  const fw = world.frameworks.find((item) => item.key === key);
  if (!fw) return null;
  const leaves = leavesOf(fw.requirements);
  const total = leaves.length;
  const covered = leaves.filter((leaf) => leaf.controls.length > 0).length;
  const passing = leaves.filter((leaf) =>
    leaf.controls.some((control) => control.status === "passing"),
  ).length;
  const prefix =
    key
      .replace(/[^a-zA-Z0-9]/g, "")
      .toUpperCase()
      .slice(0, 4) || "GAP";
  const gaps = leaves
    .filter((leaf) => leaf.controls.length === 0)
    .map((leaf, index) => ({
      code: leaf.code,
      title: leaf.title,
      summary: leaf.summary,
      suggestedControlCode: `${prefix}-${String(index + 1).padStart(2, "0")}`,
    }));
  return {
    frameworkKey: fw.key,
    frameworkName: fw.name,
    totalRequirements: total,
    coveredRequirements: covered,
    passingRequirements: passing,
    overlapPercent: total === 0 ? 0 : Math.round((covered * 100) / total),
    readinessPercent: total === 0 ? 0 : Math.round((passing * 100) / total),
    gaps,
  };
}

type Fact = { kind: string; code: string; title: string; detail: string; href: string };

function answerFromFacts(question: string, facts: Fact[], scopeLabel: string) {
  const clean = question.trim();
  if (!clean) {
    return {
      answer:
        "Ask a question about failing controls, frameworks, public exposures, attack paths, DSPM stores, FinOps savings, policies, or evidence.",
      mode: "grounded-retrieval",
      citations: [],
      suggestedRoutes: ["/controls", "/frameworks", "/exposure"],
    };
  }
  const tokens = clean
    .toLowerCase()
    .split(/[^a-z0-9._-]+/)
    .filter((token) => token.length >= 3);
  const lower = clean.toLowerCase();
  const scored = facts
    .map((fact) => {
      const hay = `${fact.kind} ${fact.code} ${fact.title} ${fact.detail}`.toLowerCase();
      let score = 0;
      for (const token of tokens) {
        if (fact.code.toLowerCase() === token) score += 12;
        else if (hay.includes(token)) score += 3;
      }
      if (
        (lower.includes("fail") || lower.includes("broken") || lower.includes("gap")) &&
        (hay.includes("failing") || hay.includes("open") || hay.includes("critical"))
      ) {
        score += 4;
      }
      if (
        (lower.includes("cost") || lower.includes("finops") || lower.includes("spend")) &&
        fact.kind === "FinOps"
      ) {
        score += 6;
      }
      if (
        (lower.includes("attack") || lower.includes("blast") || lower.includes("path")) &&
        fact.kind === "AttackPath"
      ) {
        score += 6;
      }
      if (
        (lower.includes("pii") || lower.includes("dspm") || lower.includes("encrypt")) &&
        fact.kind === "DSPM"
      ) {
        score += 5;
      }
      return { fact, score };
    })
    .filter((item) => item.score > 0)
    .sort((a, b) => b.score - a.score)
    .slice(0, 5);

  if (scored.length === 0) {
    const fallback = facts.slice(0, 3);
    return {
      answer: `No direct match was found in ${scopeLabel} for "${clean}". Top visible items: ${fallback.map((f) => `[${f.code}] ${f.title}`).join(" | ")}`,
      mode: "grounded-retrieval",
      citations: fallback.map((f) => ({
        kind: f.kind,
        code: f.code,
        title: f.title,
        href: f.href,
      })),
      suggestedRoutes: [...new Set(fallback.map((f) => f.href))],
    };
  }

  return {
    answer:
      `Grounded in ${scopeLabel} (${scored.length} matched records):\n` +
      scored.map((s) => `• [${s.fact.code}] ${s.fact.title} — ${s.fact.detail}`).join("\n"),
    mode: "grounded-retrieval",
    citations: scored.map((s) => ({
      kind: s.fact.kind,
      code: s.fact.code,
      title: s.fact.title,
      href: s.fact.href,
    })),
    suggestedRoutes: [...new Set(scored.map((s) => s.fact.href))],
  };
}

export async function handlePublicTrustEnterprise(
  method: string,
  path: string,
  query: URLSearchParams,
  body: Record<string, unknown>,
  world: DemoWorld,
): Promise<Response | null> {
  const prefix = "/api/v1/public/trust/";
  if (!path.startsWith(prefix)) return null;
  const parts = decodeURIComponent(path.slice(prefix.length)).split("/");
  const [slug, section, docId, sub] = parts;
  if (slug !== company.slug) return null;
  const trust = trustStateOf(world);
  if (!trust.published) return json({ code: "not_found" }, 404);

  if (method === "GET" && section === "receipt") {
    const issuedAtUtc = new Date().toISOString();
    const frameworks = world.frameworks
      .map((fw) => {
        const controls = world.controls.filter((c) =>
          c.mappings.some((m) => m.framework === fw.key),
        );
        const total = controls.length;
        const passing = controls.filter((c) => c.status === "passing").length;
        return {
          key: fw.key,
          name: fw.name,
          passing,
          total,
          percent: total === 0 ? 0 : Math.round((passing * 100) / total),
        };
      })
      .sort((a, b) => a.key.localeCompare(b.key));
    const latestHash =
      world.evidence[0]?.sha256 ??
      "a4f2d18c7e9032bc5d1481e0b87a561c002f3e4419d2b5c8a1023f7e8a9c4b11";
    const chainCount = world.evidence.length;
    const fwSummary = frameworks.map((f) => `${f.key}:${f.passing}/${f.total}`).join(";");
    const canonicalPayload = [
      "d2d-trust-receipt-v1",
      company.slug,
      company.name,
      company.region,
      "6",
      issuedAtUtc,
      fwSummary,
      String(chainCount),
      String(chainCount),
      latestHash,
      "Verified",
    ].join("|");
    const receiptSha256 = await sha256Hex(canonicalPayload);
    return json({
      slug: company.slug,
      tenantName: company.name,
      region: company.region,
      retentionYears: 6,
      issuedAtUtc,
      frameworks,
      chainEntryCount: chainCount,
      latestChainSequence: chainCount,
      chainHeadHash: latestHash,
      chainStatus: "Verified",
      canonicalPayload,
      receiptSha256,
    });
  }

  if (method === "GET" && section === "documents" && docId && sub === "download") {
    const doc = trust.documents.find((item) => item.id === docId);
    if (!doc) return json({ code: "not_found" }, 404);
    if (doc.access === "Nda") {
      const token = (query.get("token") ?? "").trim();
      const ok =
        token.length > 0 &&
        trust.requests.some(
          (req) =>
            req.status === "Approved" &&
            req.accessToken === token &&
            (!req.documentId || req.documentId === doc.id),
        );
      if (!ok) return json({ code: "nda_required" }, 403);
    }
    if (doc.contentBase64) {
      const binary = Uint8Array.from(atob(doc.contentBase64), (ch) => ch.charCodeAt(0));
      return new Response(binary, {
        status: 200,
        headers: {
          "Content-Type": doc.contentType ?? "application/octet-stream",
          "Content-Disposition": `attachment; filename="${doc.fileName}"`,
        },
      });
    }
    const bytes = plainPdf([
      `${company.name} — Trust Center Document`,
      `Title: ${doc.title}`,
      `Access: ${doc.access}`,
      `Updated: ${doc.updatedOn}`,
      "",
      doc.summary,
    ]);
    const copy = new ArrayBuffer(bytes.byteLength);
    new Uint8Array(copy).set(bytes);
    return new Response(copy, {
      status: 200,
      headers: {
        "Content-Type": "application/pdf",
        "Content-Disposition": `attachment; filename="${doc.fileName}"`,
      },
    });
  }

  if (method === "POST" && section === "ask") {
    const question = String(body.question ?? "").trim();
    if (!question) return json({ code: "invalid_question" }, 400);
    const facts: Fact[] = [
      {
        kind: "TrustProfile",
        code: company.slug,
        title: trust.headline,
        detail: `${trust.summary} Region: ${company.region}. Retention: 6 years. Contact: ${trust.contactEmail}.`,
        href: `/trust/${company.slug}`,
      },
      {
        kind: "EvidenceChain",
        code: "CHAIN",
        title: "Tamper-evident evidence hash chain",
        detail: `${world.evidence.length} SHA-256 hash-chained evidence entries verified with 6-year retention.`,
        href: `/trust/${company.slug}`,
      },
      ...world.frameworks.map((fw) => {
        const controls = world.controls.filter((c) =>
          c.mappings.some((m) => m.framework === fw.key),
        );
        const passing = controls.filter((c) => c.status === "passing").length;
        return {
          kind: "Framework",
          code: fw.key.toUpperCase(),
          title: fw.name,
          detail: `${passing} of ${controls.length} mapped controls passing.`,
          href: `/trust/${company.slug}`,
        };
      }),
      ...trust.documents
        .filter((d) => d.access === "Public")
        .map((d) => ({
          kind: "PublicDoc",
          code: d.fileName,
          title: d.title,
          detail: d.summary,
          href: `/trust/${company.slug}`,
        })),
    ];
    return json(answerFromFacts(question, facts, `${company.name} public Trust Center`));
  }

  return null;
}

export function handleEnterprise(
  method: string,
  path: string,
  body: Record<string, unknown>,
  world: DemoWorld,
  actor: Actor,
): Response | null {
  if (path === "/api/v1/frameworks/custom" && method === "POST") {
    if (!canWrite(actor)) return json({ code: "forbidden" }, 403);
    const key = String(body.key ?? "")
      .trim()
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, "-")
      .replace(/^-|-$/g, "");
    const name = String(body.name ?? "").trim();
    const version = String(body.version ?? "1.0").trim() || "1.0";
    const description = String(body.description ?? "").trim();
    const csv = String(body.requirementsCsv ?? "");
    if (key.length < 2 || !name || !csv.trim()) return json({ code: "invalid_framework" }, 400);
    if (world.frameworks.some((fw) => fw.key === key)) {
      return json({ code: "duplicate_framework" }, 409);
    }
    const lines = csv
      .replace(/\r\n/g, "\n")
      .split("\n")
      .map((l) => l.trim())
      .filter(Boolean);
    const start = lines[0]?.toLowerCase().startsWith("code,") ? 1 : 0;
    const reqs: DemoNode[] = [];
    for (let i = start; i < lines.length; i++) {
      const [code, title, ...rest] = lines[i].split(",");
      if (!code?.trim() || !title?.trim()) continue;
      reqs.push({
        code: code.trim(),
        title: title.trim(),
        summary: (rest.join(",").trim() || title.trim()).replace(/shall/gi, "must"),
        controls: [],
        children: [],
      });
    }
    if (reqs.length === 0) return json({ code: "invalid_framework" }, 400);
    world.frameworks.push({
      key,
      name,
      version,
      description: description || `Custom framework ${name} (${version}).`,
      requirements: reqs,
    });
    return json({ key, name, requirementCount: reqs.length });
  }

  if (path.startsWith("/api/v1/frameworks/") && path.endsWith("/simulator") && method === "GET") {
    const key = decodeURIComponent(path.slice("/api/v1/frameworks/".length, -"/simulator".length));
    if (actor.role === "Auditor" && key !== "soc2") return json({ code: "forbidden" }, 403);
    const sim = simulateFramework(world, key);
    return sim ? json(sim) : json({ code: "not_found" }, 404);
  }

  if (
    path.startsWith("/api/v1/frameworks/") &&
    path.endsWith("/simulator/adopt") &&
    method === "POST"
  ) {
    if (!canWrite(actor)) return json({ code: "forbidden" }, 403);
    const key = decodeURIComponent(
      path.slice("/api/v1/frameworks/".length, -"/simulator/adopt".length),
    );
    const fw = world.frameworks.find((item) => item.key === key);
    const sim = simulateFramework(world, key);
    if (!fw || !sim) return json({ code: "not_found" }, 404);
    const wanted = Array.isArray(body.requirementCodes)
      ? new Set(body.requirementCodes.map((c) => String(c)))
      : null;
    const toAdopt = sim.gaps.filter((gap) => !wanted || wanted.has(gap.code));
    const leaves = leavesOf(fw.requirements);
    let created = 0;
    for (const gap of toAdopt) {
      let code = gap.suggestedControlCode;
      let n = 1;
      while (world.controls.some((c) => c.code === code)) {
        code = `${gap.suggestedControlCode}-${n++}`;
      }
      world.controls.push({
        code,
        title: gap.title,
        description: gap.summary,
        status: "needsEvidence",
        frequency: "Quarterly",
        ownerName: actor.name,
        ownerEmail: actor.email,
        mappings: [
          {
            framework: fw.key,
            requirement: gap.code,
            coverage: "full",
            frameworkName: fw.name,
          },
        ],
        tests: [],
      });
      const leaf = leaves.find((item) => item.code === gap.code);
      if (leaf) {
        leaf.controls.push({
          code,
          title: gap.title,
          coverage: "full",
          status: "needsEvidence",
        });
      }
      created++;
    }
    return json(created);
  }

  if (path === "/api/v1/assistant/ask" && method === "POST") {
    const question = String(body.question ?? "").trim();
    if (!question) return json({ code: "invalid_question" }, 400);
    if (actor.role === "Auditor") {
      const soc2Controls = world.controls.filter((c) =>
        c.mappings.some((m) => m.framework === "soc2"),
      );
      const facts: Fact[] = soc2Controls.map((c) => ({
        kind: "Control",
        code: c.code,
        title: c.title,
        detail: `Status: ${c.status}. ${c.description}`,
        href: `/controls/${encodeURIComponent(c.code)}`,
      }));
      return json(answerFromFacts(question, facts, "auditor scope (soc2)"));
    }
    const facts: Fact[] = [
      ...world.controls.map((c) => ({
        kind: "Control",
        code: c.code,
        title: c.title,
        detail: `Status: ${c.status}. Owner: ${c.ownerName}. ${c.description}`,
        href: `/controls/${encodeURIComponent(c.code)}`,
      })),
      ...world.checks
        .filter((ch) => ch.outcome === "fail")
        .map((ch) => ({
          kind: "Test",
          code: ch.controlCode,
          title: ch.name,
          detail: `Failing check: ${ch.detail} Remediation: ${ch.remediation}`,
          href: "/tests",
        })),
      {
        kind: "Exposure",
        code: "CFG-01",
        title: "AWS sg-staging-analytics-mongo (MongoDB:27017)",
        detail: "Open Critical exposure from 0.0.0.0/0 (risk 98/100). 1-click Terraform available.",
        href: "/exposure",
      },
      {
        kind: "AttackPath",
        code: "AP-01",
        title: "Internet MongoDB (sg-staging-analytics-mongo) to harborline-staging-exports",
        detail: "Blast radius 98/100 (Critical). Close 0.0.0.0/0 port 27017 first.",
        href: "/attack-paths",
      },
      {
        kind: "DSPM",
        code: "CFG-01",
        title: "harborline-staging-exports (Restricted PII/SSN)",
        detail: "AWS S3 unencrypted & public with 480,000 records (DSPM risk 100).",
        href: "/dspm",
      },
      {
        kind: "FinOps",
        code: "CR-02",
        title: "$2,075/mo ($24,900/yr) open cloud savings across 5 compliance-linked findings",
        detail: "Includes staging S3 lifecycle ($640/mo) and overprovisioned Azure PG ($890/mo).",
        href: "/finops",
      },
      ...world.policies.map((p) => ({
        kind: "Policy",
        code: p.code,
        title: p.title,
        detail: `Policy owner: ${p.ownerName}.`,
        href: "/policies",
      })),
    ];
    return json(answerFromFacts(question, facts, `${company.name} workspace`));
  }

  if (path.startsWith("/api/v1/developer/")) {
    if (actor.role !== "Admin") return json({ code: "forbidden" }, 403);
    const state = getState(world);
    if (path === "/api/v1/developer/keys" && method === "GET") return json(state.keys);
    if (path === "/api/v1/developer/keys" && method === "POST") {
      const name = String(body.name ?? "").trim();
      const scopes = String(body.scopes ?? "read:controls,read:evidence").trim();
      if (!name) return json({ code: "invalid_api_key" }, 400);
      const plaintextKey = `d2d_api_${crypto.randomUUID().replaceAll("-", "")}`;
      const key: DemoApiKey = {
        id: crypto.randomUUID(),
        name,
        prefix: plaintextKey.slice(0, 16),
        scopes,
        createdByEmail: actor.email,
        createdAt: new Date().toISOString(),
        revokedAt: null,
      };
      state.keys.unshift(key);
      return json({ key, plaintextKey });
    }
    if (path.startsWith("/api/v1/developer/keys/") && method === "DELETE") {
      const id = path.slice("/api/v1/developer/keys/".length);
      const row = state.keys.find((item) => item.id === id);
      if (!row) return json({ code: "not_found" }, 404);
      row.revokedAt = new Date().toISOString();
      return json(true);
    }
    if (path === "/api/v1/developer/webhooks" && method === "GET") return json(state.webhooks);
    if (path === "/api/v1/developer/webhooks" && method === "POST") {
      const name = String(body.name ?? "").trim();
      const url = String(body.url ?? "").trim();
      const events = String(body.events ?? "test.failed,exposure.critical").trim();
      if (!name || !/^https?:\/\//i.test(url)) return json({ code: "invalid_webhook" }, 400);
      const webhook: DemoWebhook = {
        id: crypto.randomUUID(),
        name,
        url,
        events,
        enabled: true,
        createdAt: new Date().toISOString(),
      };
      state.webhooks.unshift(webhook);
      return json({ webhook, signingSecret: `whsec_${crypto.randomUUID().replaceAll("-", "")}` });
    }
    if (path.startsWith("/api/v1/developer/webhooks/") && method === "DELETE") {
      const id = path.slice("/api/v1/developer/webhooks/".length);
      const idx = state.webhooks.findIndex((item) => item.id === id);
      if (idx < 0) return json({ code: "not_found" }, 404);
      state.webhooks.splice(idx, 1);
      return json(true);
    }
  }

  return null;
}
