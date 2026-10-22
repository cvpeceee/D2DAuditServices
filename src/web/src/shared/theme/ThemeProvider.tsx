import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from "react";

export type ThemeChoice = "light" | "dark" | "system" | "high-contrast";

const ThemeContext = createContext<{
  choice: ThemeChoice;
  setChoice: (choice: ThemeChoice) => void;
}>({
  choice: "system",
  setChoice: () => undefined,
});

function resolve(choice: ThemeChoice): string {
  if (choice !== "system") return choice;
  if (window.matchMedia("(prefers-contrast: more)").matches) return "high-contrast";
  return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [choice, setChoiceState] = useState<ThemeChoice>(() => {
    const stored = localStorage.getItem("d2d-theme");
    return stored === "light" ||
      stored === "dark" ||
      stored === "system" ||
      stored === "high-contrast"
      ? stored
      : "system";
  });

  useEffect(() => {
    const apply = () => {
      document.documentElement.dataset.theme = resolve(choice);
    };
    apply();
    const media = window.matchMedia("(prefers-color-scheme: dark)");
    const contrast = window.matchMedia("(prefers-contrast: more)");
    media.addEventListener("change", apply);
    contrast.addEventListener("change", apply);
    return () => {
      media.removeEventListener("change", apply);
      contrast.removeEventListener("change", apply);
    };
  }, [choice]);

  const value = useMemo(
    () => ({
      choice,
      setChoice: (next: ThemeChoice) => {
        localStorage.setItem("d2d-theme", next);
        setChoiceState(next);
      },
    }),
    [choice],
  );
  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}

export function useTheme() {
  return useContext(ThemeContext);
}
