import { useEffect, type ReactNode } from "react";
import { Navigate, Route, Routes, useLocation } from "react-router-dom";
import { useAuth } from "./features/auth/AuthContext";
import { Skeleton } from "./shared/ui/ui";
import { ActivityPage } from "./features/activity/ActivityPage";
import { AuthCallbackPage } from "./features/auth/AuthCallbackPage";
import { ControlsPage } from "./features/controls/ControlsPage";
import { DashboardPage } from "./features/dashboard/DashboardPage";
import { FrameworkDetailPage } from "./features/frameworks/FrameworkDetailPage";
import { FrameworksPage } from "./features/frameworks/FrameworksPage";
import { LoginPage } from "./features/auth/LoginPage";
import { EvidencePage } from "./features/evidence/EvidencePage";
import { IntegrationsPage } from "./features/integrations/IntegrationsPage";
import { PlaceholderPage } from "./features/shell/PlaceholderPage";
import { TestsPage } from "./features/tests/TestsPage";
import { AppShell } from "./features/shell/AppShell";

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
      <Route
        path="/login"
        element={
          <GuestOnly>
            <LoginPage />
          </GuestOnly>
        }
      />
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
        <Route path="tests" element={<TestsPage />} />
        <Route path="evidence" element={<EvidencePage />} />
        <Route path="policies" element={<PlaceholderPage />} />
        <Route path="risks" element={<PlaceholderPage />} />
        <Route path="vendors" element={<PlaceholderPage />} />
        <Route path="people" element={<PlaceholderPage />} />
        <Route path="auditor" element={<PlaceholderPage />} />
        <Route path="integrations" element={<IntegrationsPage />} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
