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
import { AuditorPage } from "./features/auditor/AuditorPage";
import { EvidencePage } from "./features/evidence/EvidencePage";
import { PoliciesPage } from "./features/policies/PoliciesPage";
import { IntegrationsPage } from "./features/integrations/IntegrationsPage";
import { AccessReviewsPage } from "./features/access/AccessReviewsPage";
import { PeoplePage } from "./features/people/PeoplePage";
import { RisksPage } from "./features/risks/RisksPage";
import { SoaPage } from "./features/soa/SoaPage";
import { VendorsPage } from "./features/vendors/VendorsPage";
import { TestsPage } from "./features/tests/TestsPage";
import { AppShell } from "./features/shell/AppShell";
import { AssetsPage } from "./features/program/AssetsPage";
import { MePage } from "./features/program/MePage";
import { SettingsPage } from "./features/program/SettingsPage";
import { TasksPage } from "./features/program/TasksPage";
import { TrainingPage } from "./features/program/TrainingPage";

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
        <Route path="policies" element={<PoliciesPage />} />
        <Route path="risks" element={<RisksPage />} />
        <Route path="soa" element={<SoaPage />} />
        <Route path="vendors" element={<VendorsPage />} />
        <Route path="people" element={<PeoplePage />} />
        <Route path="me" element={<MePage />} />
        <Route path="training" element={<TrainingPage />} />
        <Route path="assets" element={<AssetsPage />} />
        <Route path="tasks" element={<TasksPage />} />
        <Route path="settings" element={<SettingsPage />} />
        <Route path="access-reviews" element={<AccessReviewsPage />} />
        <Route path="auditor" element={<AuditorPage />} />
        <Route path="integrations" element={<IntegrationsPage />} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
