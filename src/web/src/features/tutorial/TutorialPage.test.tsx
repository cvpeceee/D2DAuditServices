import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import "../../shared/i18n";
import {
  DEEP_LINK_MODULES,
  GAP_TRACKER_ITEMS,
  SUPER_APP_MILESTONES,
  TUTORIAL_CHAPTERS,
  TutorialPage,
} from "./TutorialPage";

describe("TutorialPage interactive product tour & 1080p video/audio tutorial center", () => {
  const originalFetch = window.fetch;

  beforeEach(() => {
    window.fetch = vi.fn(async () => {
      return new Response(
        JSON.stringify({
          chapters: TUTORIAL_CHAPTERS.map((c) => ({
            number: c.number,
            startTime: c.startTime,
            endTime: c.endTime,
            duration: c.duration,
          })),
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }) as typeof window.fetch;
  });

  afterEach(() => {
    cleanup();
    window.fetch = originalFetch;
    vi.restoreAllMocks();
  });

  it("renders 100% Super-App completion dashboard across all 18 Gap Tracker items and M1-M10 milestones", async () => {
    const user = userEvent.setup();
    render(
      <MemoryRouter initialEntries={["/tutorial"]}>
        <TutorialPage />
      </MemoryRouter>,
    );

    const progressbar = screen.getByRole("progressbar", {
      name: /Super-App Overall Completion/i,
    });
    expect(progressbar).toHaveAttribute("aria-valuenow", "100");
    expect(screen.getByText("100% Super-App Complete")).toBeInTheDocument();
    expect(screen.getByText("18 / 18 Gap Tracker Items Done")).toBeInTheDocument();

    for (const milestone of SUPER_APP_MILESTONES) {
      expect(screen.getByText(milestone.code)).toBeInTheDocument();
    }
    expect(GAP_TRACKER_ITEMS).toHaveLength(18);
    expect(screen.getByText("#18")).toBeInTheDocument();

    // Filter gap tracker by P3
    await user.click(screen.getByRole("button", { name: "P3" }));
    expect(screen.getByText("#18")).toBeInTheDocument();
    expect(screen.queryByText("#1")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "All 18 Items" }));
    expect(screen.getByText("#1")).toBeInTheDocument();
  });

  it("seeks the embedded HTML5 video player when chapter jump buttons are clicked and updates simulator state", async () => {
    const user = userEvent.setup();
    render(
      <MemoryRouter initialEntries={["/tutorial"]}>
        <TutorialPage />
      </MemoryRouter>,
    );

    const video = screen.getByLabelText(
      /D2D Audit Services Super App 1080p Video Tutorial/i,
    ) as HTMLVideoElement;
    expect(video.tagName).toBe("VIDEO");
    expect(video.getAttribute("src")).toContain("tutorial/d2d-super-app-tutorial.mp4");

    // Click Chapter 6 jump button
    const ch6Button = screen.getByRole("button", {
      name: /Ch 6: Trust Center v2 & Cryptographic Receipts/i,
    });
    await user.click(ch6Button);

    expect(ch6Button).toHaveAttribute("aria-pressed", "true");
    expect(video.currentTime).toBeCloseTo(TUTORIAL_CHAPTERS[5].startTime, 1);
    expect(screen.getByText(/Chapter 6: Trust Center v2 — Clickwrap NDA/i)).toBeInTheDocument();
  });

  it("steps through packet flows, toggles Web Speech API voiceover narration, and exposes deep-links to all 9 live modules", async () => {
    const speakMock = vi.fn();
    const cancelMock = vi.fn();
    Object.defineProperty(window, "speechSynthesis", {
      configurable: true,
      value: {
        speak: speakMock,
        cancel: cancelMock,
      },
    });

    const user = userEvent.setup();
    render(
      <MemoryRouter initialEntries={["/tutorial"]}>
        <TutorialPage />
      </MemoryRouter>,
    );

    // Step packet flow
    await user.click(screen.getByRole("button", { name: "Step Packet Flow" }));
    expect(screen.getByText(/Stage 2 of 4/i)).toBeInTheDocument();

    // Switch packet flow preset to Internet Exposure -> Leaked Secret -> Identity Gap -> PII Store Attack Path
    const attackFlowBtn = screen.getByRole("button", {
      name: /Internet Exposure -> Leaked Secret -> Identity Gap -> PII Store Attack Path/i,
    });
    await user.click(attackFlowBtn);
    expect(attackFlowBtn).toHaveAttribute("aria-pressed", "true");
    expect(
      screen.getByText(/Detects internet-exposed security group sg-0a9f21e4/i),
    ).toBeInTheDocument();

    // Toggle voiceover narration
    const voiceBtn = screen.getByRole("button", { name: /Start Voiceover Narration/i });
    await user.click(voiceBtn);
    await waitFor(() => {
      expect(screen.getByText(/Narrating Chapter 1/i)).toBeInTheDocument();
    });

    // Verify all 9 required live module deep-links exist
    const expectedRoutes = [
      "/frameworks",
      "/exposure",
      "/attack-paths",
      "/dspm",
      "/finops",
      "/trust/harborline",
      "/auditor",
      "/governance-hub",
      "/settings",
    ];
    expect(DEEP_LINK_MODULES.map((m) => m.route)).toEqual(expectedRoutes);

    const hrefs = screen
      .getAllByRole("link")
      .map((el) => el.getAttribute("href"))
      .filter(Boolean);
    for (const route of expectedRoutes) {
      expect(hrefs).toContain(route);
    }
  });
});
