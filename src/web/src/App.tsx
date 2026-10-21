import { useEffect, type ReactNode } from "react";
import { Navigate, Route, Routes, useLocation } from "react-router-dom";
import { useAuth } from "./auth/AuthContext";
import { Skeleton } from "./components/ui";
import { ActivityPage } from "./pages/ActivityPage";
import { AuthCallbackPage } from "./pages/AuthCallbackPage";
import { ControlsPage } from "./pages/ControlsPage";
import { DashboardPage } from "./pages/DashboardPage";
import { FrameworkDetailPage } from "./pages/FrameworkDetailPage";
import { FrameworksPage } from "./pages/FrameworksPage";
import { LoginPage } from "./pages/LoginPage";
import { PlaceholderPage } from "./pages/PlaceholderPage";
import { AppShell } from "./shell/AppShell";

function RequireAuth({ children }: { children: ReactNode }) {
  const { ready, user } = useAuth();
  const location = useLocation();
  if (!ready) {
    return (
      <div className="content">
        <Skeleton className="lg" />
      </div>
    );
  }
  if (!user) return <Navigate to="/login" replace state={{ from: location.pathname }} />;
  return children;
}

function GuestOnly({ children }: { children: ReactNode }) {
  const { ready, user } = useAuth();
  if (!ready) {
    return (
      <div className="login-stage">
        <Skeleton className="lg" />
      </div>
    );
  }
  if (user) return <Navigate to="/" replace />;
  return children;
}

export function App() {
  const { user } = useAuth();
  useEffect(() => {
    document.title = "D2D Audit Services";
  }, [user]);

  return (
    <Routes>
      <Route path="/login" element={<GuestOnly><LoginPage /></GuestOnly>} />
      <Route path="/auth/callback" element={<AuthCallbackPage />} />
      <Route
        element={
          <RequireAuth>
            <AppShell />
          </RequireAuth>
        }
      >
        <Route index element={<DashboardPage />} />
        <Route path="frameworks" element={<FrameworksPage />} />
        <Route path="frameworks/:key" element={<FrameworkDetailPage />} />
        <Route path="controls" element={<ControlsPage />} />
        <Route path="activity" element={<ActivityPage />} />
        <Route path="tests" element={<PlaceholderPage />} />
        <Route path="evidence" element={<PlaceholderPage />} />
        <Route path="policies" element={<PlaceholderPage />} />
        <Route path="risks" element={<PlaceholderPage />} />
        <Route path="vendors" element={<PlaceholderPage />} />
        <Route path="people" element={<PlaceholderPage />} />
        <Route path="auditor" element={<PlaceholderPage />} />
        <Route path="integrations" element={<PlaceholderPage />} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
