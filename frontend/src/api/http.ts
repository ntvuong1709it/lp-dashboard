import type { ProblemDetails } from "./types";

export class ApiError extends Error {
  readonly status: number;
  /** Machine-readable reason from the server (e.g. "wrong_password"), when it sent one. */
  readonly code?: string;

  constructor(status: number, message: string, code?: string) {
    super(message);
    this.status = status;
    this.code = code;
  }
}

type SessionLostHandler = (code: "unauthenticated" | "setup_required") => void;

let onSessionLost: SessionLostHandler | null = null;

/** The auth layer registers here so any API call that comes back "not logged in" sends the app to the login screen. */
export function setSessionLostHandler(handler: SessionLostHandler | null) {
  onSessionLost = handler;
}

export async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    // The session lives in an HttpOnly cookie; no token is ever handled in script.
    credentials: "same-origin",
    ...init,
    headers: { "Content-Type": "application/json", ...init?.headers },
  });
  if (!response.ok) {
    let message = `${response.status} ${response.statusText}`;
    let code: string | undefined;
    try {
      const problem = (await response.json()) as ProblemDetails;
      message = problem.detail ?? problem.title ?? message;
      code = problem.code;
    } catch {
      // Non-JSON error body.
    }
    if (
      response.status === 401 &&
      (code === "unauthenticated" || code === "setup_required")
    ) {
      onSessionLost?.(code);
    }
    throw new ApiError(response.status, message, code);
  }
  return response.status === 204
    ? (undefined as T)
    : ((await response.json()) as T);
}
