import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { api } from "../../shared/api/client";
import { crumbsFor } from "./Breadcrumbs";
import { useGlobalSearch } from "./globalSearch";

vi.mock("../../shared/api/client", async () => {
  const actual =
    await vi.importActual<typeof import("../../shared/api/client")>("../../shared/api/client");
  return { ...actual, api: vi.fn() };
});

describe("command palette search", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.mocked(api).mockReset();
  });
  afterEach(() => vi.useRealTimers());

  it("debounces tenant-scoped searches and ignores short queries", async () => {
    vi.mocked(api).mockResolvedValue({
      query: "mfa",
      results: [{ kind: "control", key: "AC-01", title: "MFA", subtitle: "AC-01", href: "/x" }],
    });
    const { result, rerender } = renderHook(({ q }) => useGlobalSearch(q, "tenant-1", true), {
      initialProps: { q: "m" },
    });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(300);
    });
    expect(api).not.toHaveBeenCalled();

    rerender({ q: "mf" });
    rerender({ q: "mfa" });
    expect(result.current.loading).toBe(true);
    await act(async () => {
      await vi.advanceTimersByTimeAsync(200);
    });
    expect(api).toHaveBeenCalledTimes(1);
    expect(api).toHaveBeenCalledWith("/api/v1/search?q=mfa", { tenantId: "tenant-1" });
    expect(result.current.results).toHaveLength(1);
    expect(result.current.loading).toBe(false);
  });

  it("does not search while the palette is closed", async () => {
    renderHook(() => useGlobalSearch("access", "tenant-1", false));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(300);
    });
    expect(api).not.toHaveBeenCalled();
  });
});

describe("breadcrumbs", () => {
  const label = (key: string, fallback: string) => `${key}|${fallback}`;
  it("links parents and leaves the current page unlinked", () => {
    expect(crumbsFor("/controls/AC-01", label)).toEqual([
      { label: "controls|controls", to: "/controls" },
      { label: "AC-01" },
    ]);
    expect(crumbsFor("/trust-center", label)).toEqual([{ label: "trust|trust-center" }]);
    expect(crumbsFor("/", label)).toEqual([{ label: "dashboard|Dashboard" }]);
  });
});
