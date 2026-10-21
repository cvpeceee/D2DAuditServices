import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from "react";
import { api, type TenantMembership, type User } from "../../shared/api/client";

interface AuthState {
  ready: boolean;
  user: User | null;
  tenants: TenantMembership[];
  tenantId: string | null;
  setTenantId: (tenantId: string) => void;
  refresh: () => Promise<void>;
  signOut: () => Promise<void>;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [ready, setReady] = useState(false);
  const [user, setUser] = useState<User | null>(null);
  const [tenants, setTenants] = useState<TenantMembership[]>([]);
  const [tenantId, setTenantIdState] = useState<string | null>(
    sessionStorage.getItem("d2d-tenant"),
  );

  const refresh = useCallback(async () => {
    try {
      const me = await api<{ user?: User | null; tenants?: TenantMembership[] | null }>(
        "/api/v1/me",
        { skipRefresh: true },
      );
      setUser(me.user ?? null);
      const next = me.tenants ?? [];
      setTenants(next);
      setTenantIdState((current) => {
        if (current && next.some((tenant) => tenant.tenantId === current)) return current;
        return next[0]?.tenantId ?? null;
      });
    } catch {
      setUser(null);
      setTenants([]);
    } finally {
      setReady(true);
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const setTenantId = useCallback((id: string) => {
    sessionStorage.setItem("d2d-tenant", id);
    setTenantIdState(id);
  }, []);

  const signOut = useCallback(async () => {
    await api("/api/v1/auth/logout", { method: "POST", skipRefresh: true });
    sessionStorage.removeItem("d2d-tenant");
    setUser(null);
    setTenants([]);
    setTenantIdState(null);
  }, []);

  const value = useMemo(
    () => ({ ready, user, tenants, tenantId, setTenantId, refresh, signOut }),
    [ready, user, tenants, tenantId, setTenantId, refresh, signOut],
  );
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const value = useContext(AuthContext);
  if (!value) throw new Error("AuthProvider is missing");
  return value;
}
