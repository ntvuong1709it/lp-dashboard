import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  type ReactNode,
} from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { request, setSessionLostHandler } from "../api/http";
import type { AuthStatus } from "../api/types";

const STATUS_KEY = ["auth", "status"] as const;

export type AuthState = "loading" | "error" | "setup" | "login" | "ready";

interface AuthContextValue {
  state: AuthState;
  error: Error | null;
  /** Re-checks the server (used by the "try again" button when it could not be reached). */
  reload: () => void;
  /** Sets the first password (or a reset one) and signs this browser in. */
  setup: (password: string) => Promise<void>;
  login: (password: string) => Promise<void>;
  logout: () => Promise<void>;
  logoutEverywhere: () => Promise<void>;
  changePassword: (current: string, next: string) => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const client = useQueryClient();
  const status = useQuery({
    queryKey: STATUS_KEY,
    queryFn: () => request<AuthStatus>("/api/auth/status"),
    // The status only changes through the actions below or a 401 from any other call.
    staleTime: Infinity,
    retry: false,
  });

  /** Drops everything cached for the old session so no data survives a logout, then shows the right screen. */
  const reset = useCallback(
    (next: AuthStatus) => {
      client.removeQueries({ predicate: (q) => q.queryKey[0] !== STATUS_KEY[0] });
      client.setQueryData<AuthStatus>(STATUS_KEY, next);
    },
    [client],
  );

  useEffect(() => {
    setSessionLostHandler((code) =>
      reset({ setupRequired: code === "setup_required", authenticated: false }),
    );
    return () => setSessionLostHandler(null);
  }, [reset]);

  const value = useMemo<AuthContextValue>(() => {
    const signedIn = () =>
      client.setQueryData<AuthStatus>(STATUS_KEY, {
        setupRequired: false,
        authenticated: true,
      });
    const post = (path: string, body?: unknown) =>
      request<void>(path, {
        method: "POST",
        body: body === undefined ? undefined : JSON.stringify(body),
      });

    const state: AuthState = status.isPending
      ? "loading"
      : status.isError
        ? "error"
        : status.data.setupRequired
          ? "setup"
          : status.data.authenticated
            ? "ready"
            : "login";

    return {
      state,
      error: status.error,
      reload: () => void status.refetch(),
      setup: async (password) => {
        await post("/api/auth/setup", { password });
        signedIn();
      },
      login: async (password) => {
        await post("/api/auth/login", { password });
        signedIn();
      },
      logout: async () => {
        await post("/api/auth/logout").catch(() => undefined);
        reset({ setupRequired: false, authenticated: false });
      },
      logoutEverywhere: async () => {
        await post("/api/auth/logout-all");
        reset({ setupRequired: false, authenticated: false });
      },
      changePassword: (current, next) =>
        post("/api/auth/change-password", { current, new: next }),
    };
  }, [client, reset, status]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const value = useContext(AuthContext);
  if (!value) throw new Error("useAuth must be used inside <AuthProvider>");
  return value;
}

export const MIN_PASSWORD_LENGTH = 8;
