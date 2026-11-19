import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import "../../shared/i18n";
import { TrustPublicPage } from "./TrustPublicPage";

describe("TrustPublicPage NDA downloads", () => {
  const originalFetch = window.fetch;

  beforeEach(() => {
    URL.createObjectURL = vi.fn(() => "blob:mock");
    URL.revokeObjectURL = vi.fn();
  });

  afterEach(() => {
    window.fetch = originalFetch;
    vi.restoreAllMocks();
  });

  it("shows nda_required alert on 403 and sends approved token via X-Trust-Token header without bogus X-Tenant-Id", async () => {
    const calls: { url: string; headers: Headers }[] = [];
    window.fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === "string" ? input : input.toString();
      const headers = new Headers(init?.headers);
      calls.push({ url, headers });
      if (url === "/api/v1/public/trust/acme") {
        return new Response(
          JSON.stringify({
            slug: "acme",
            tenantName: "Acme Corp",
            headline: "Acme Trust",
            summary: "Security overview",
            contactEmail: "sec@acme.example",
            frameworks: [],
            documents: [
              {
                id: "doc-nda",
                title: "SOC 2 Report",
                summary: "Annual report",
                access: "Nda",
                fileName: "",
                updatedOn: "2026-06-01",
              },
            ],
          }),
          { status: 200, headers: { "Content-Type": "application/json" } },
        );
      }
      if (url.endsWith("/receipt")) {
        return new Response(JSON.stringify({ code: "not_found" }), { status: 404 });
      }
      if (url.endsWith("/documents/doc-nda/download")) {
        if (headers.get("X-Trust-Token") === "secret-token-123") {
          return new Response("pdf-bytes", {
            status: 200,
            headers: {
              "Content-Type": "application/pdf",
              "Content-Disposition": 'attachment; filename="soc2.pdf"',
            },
          });
        }
        return new Response(JSON.stringify({ code: "nda_required" }), {
          status: 403,
          headers: { "Content-Type": "application/json" },
        });
      }
      return new Response(null, { status: 404 });
    }) as typeof window.fetch;

    const user = userEvent.setup();
    render(
      <MemoryRouter initialEntries={["/trust/acme"]}>
        <Routes>
          <Route path="/trust/:slug" element={<TrustPublicPage />} />
        </Routes>
      </MemoryRouter>,
    );

    await screen.findAllByText("SOC 2 Report");
    await user.click(screen.getByRole("button", { name: "Download" }));

    const alert = await screen.findByRole("alert");
    expect(alert.textContent).toContain("approved NDA download token");

    const firstDownload = calls.find((c) => c.url.endsWith("/documents/doc-nda/download"))!;
    expect(firstDownload.headers.get("X-Tenant-Id")).toBeNull();
    expect(firstDownload.url).not.toContain("token=");

    await user.type(screen.getByPlaceholderText(/approval email/i), "secret-token-123");
    await user.click(screen.getByRole("button", { name: "Download" }));

    await waitFor(() => {
      const approved = calls.filter((c) => c.url.endsWith("/documents/doc-nda/download"))[1];
      expect(approved).toBeDefined();
      expect(approved.headers.get("X-Trust-Token")).toBe("secret-token-123");
      expect(approved.headers.get("X-Tenant-Id")).toBeNull();
      expect(approved.url).not.toContain("secret-token-123");
    });
  });
});
