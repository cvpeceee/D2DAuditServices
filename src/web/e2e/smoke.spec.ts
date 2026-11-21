import { expect, test, type Page } from "@playwright/test";

const admin = "chandra.vell@harborline.example";

// Every authenticated route in src/App.tsx (parameterised ones use demo data).
const routes = [
  "/",
  "/frameworks",
  "/frameworks/soc2",
  "/controls",
  "/activity",
  "/tests",
  "/evidence",
  "/policies",
  "/risks",
  "/soa",
  "/vendors",
  "/people",
  "/people/computers",
  "/vulnerabilities",
  "/incidents",
  "/changes",
  "/questionnaires",
  "/system-description",
  "/continuity",
  "/exposure",
  "/dspm",
  "/finops",
  "/attack-paths",
  "/trust-center",
  "/me",
  "/training",
  "/assets",
  "/tasks",
  "/settings",
  "/access-reviews",
  "/auditor",
  "/integrations",
  "/governance-hub",
  "/tutorial",
];

// The activity log intentionally shows machine action codes such as "policy.approved".
const rawKeyExempt = new Set(["/activity"]);

// Text that looks like an untranslated i18n key, e.g. "settingsPage.lastUsed".
const rawKey = /^[a-z][a-zA-Z0-9]*(\.[a-zA-Z0-9_]+)+$/;
const allowedDotted = /\.(example|com|io|json|pdf|csv|zip|txt|md|tf|ya?ml|xlsx|docx|net|org)$/;

function collectProblems(page: Page) {
  const problems: string[] = [];
  page.on("pageerror", (error) => problems.push(`pageerror: ${error.message}`));
  page.on("console", (message) => {
    if (message.type() === "error") problems.push(`console: ${message.text()}`);
  });
  return problems;
}

async function signIn(page: Page, problems: string[]) {
  await page.goto("./");
  await page.getByRole("button", { name: `Sign in as ${admin}` }).click();
  await expect(page.locator("main")).toBeVisible();
  // The anonymous GET /api/v1/me before sign-in is an expected 401.
  problems.length = 0;
}

async function pageHealth(page: Page) {
  return page.evaluate(
    ({ keyPattern, allowedPattern }) => {
      const key = new RegExp(keyPattern);
      const allowed = new RegExp(allowedPattern);
      const raw: string[] = [];
      const walker = document.createTreeWalker(
        document.querySelector("main") ?? document.body,
        NodeFilter.SHOW_TEXT,
      );
      while (walker.nextNode()) {
        const text = walker.currentNode.textContent?.trim() ?? "";
        if (key.test(text) && !allowed.test(text)) raw.push(text);
      }
      return {
        raw,
        overflow: document.documentElement.scrollWidth > window.innerWidth + 1,
        badText: /\bundefined\b|\bNaN\b|\[object Object\]/.test(document.body.innerText),
      };
    },
    { keyPattern: rawKey.source, allowedPattern: allowedDotted.source },
  );
}

test("demo admin can visit every route without errors", async ({ page }) => {
  const problems = collectProblems(page);
  await signIn(page, problems);

  for (const route of routes) {
    await test.step(route, async () => {
      const failures: string[] = [];
      const onResponse = (response: { url(): string; status(): number }) => {
        const url = new URL(response.url());
        if (url.pathname.startsWith("/api/") && response.status() >= 400)
          failures.push(`${response.status()} ${url.pathname}`);
      };
      page.on("response", onResponse);
      await page.goto(`./#${route}`);
      await expect(page.locator("main h1").first()).toBeVisible();
      await page.waitForTimeout(800);
      page.off("response", onResponse);

      const health = await pageHealth(page);
      if (!rawKeyExempt.has(route))
        expect.soft(health.raw, `untranslated keys on ${route}`).toEqual([]);
      expect.soft(health.overflow, `horizontal overflow on ${route}`).toBe(false);
      expect.soft(health.badText, `undefined/NaN text on ${route}`).toBe(false);
      expect.soft(failures, `failed API calls on ${route}`).toEqual([]);
      expect.soft(problems, `errors on ${route}`).toEqual([]);
      problems.length = 0;
    });
  }
});

test("a rejected save shows an error instead of an unhandled rejection", async ({ page }) => {
  const problems = collectProblems(page);
  await signIn(page, problems);
  await page.goto("./#/changes");
  await page.getByRole("button", { name: "Record a change" }).click();
  await expect(page.getByRole("alert")).toContainText("could not be saved");
  expect(problems.filter((line) => line.startsWith("pageerror"))).toEqual([]);
});
