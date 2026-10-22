import "@fontsource/inter/400.css";
import "@fontsource/inter/500.css";
import "@fontsource/inter/600.css";
import "@fontsource/inter/700.css";
import "@fontsource/inter/800.css";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter, HashRouter } from "react-router-dom";
import { App } from "./App";
import { AuthProvider } from "./features/auth/AuthContext";
import "./shared/i18n";
import "./styles/app.css";
import { ThemeProvider } from "./shared/theme/ThemeProvider";

const demo = import.meta.env.VITE_DEMO === "true";
if (demo) {
  const { installDemoBackend } = await import("./demo/install");
  installDemoBackend();
}

const Router = demo ? HashRouter : BrowserRouter;

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <ThemeProvider>
      <Router>
        <AuthProvider>
          <App />
        </AuthProvider>
      </Router>
    </ThemeProvider>
  </StrictMode>,
);
