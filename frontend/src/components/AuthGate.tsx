import { useState, type FormEvent, type ReactNode } from "react";
import { ApiError } from "../api/http";
import { MIN_PASSWORD_LENGTH, useAuth } from "../lib/auth";

const inputClass =
  "w-full rounded-md border border-slate-700 bg-slate-950 px-3 py-2 text-sm";
const buttonClass =
  "rounded-md bg-sky-600 px-4 py-2 text-sm font-medium text-white hover:bg-sky-500 disabled:opacity-50";

/**
 * Shows the app only for a logged-in browser. Otherwise: the "set your password" screen on a brand-new server (or after
 * a reset), or the sign-in screen. Nothing else mounts before that, so no data request is made while logged out.
 */
export function AuthGate({ children }: { children: ReactNode }) {
  const { state, error, reload } = useAuth();

  switch (state) {
    case "ready":
      return <>{children}</>;
    case "setup":
      return <CreatePasswordScreen />;
    case "login":
      return <LoginScreen />;
    case "error":
      return (
        <Card title="Cannot reach the server">
          <p className="text-sm text-slate-400">
            {error?.message ?? "The server did not answer."}
          </p>
          <button className={buttonClass} onClick={reload}>
            Try again
          </button>
        </Card>
      );
    default:
      return <div className="p-10 text-center text-sm text-slate-500">Loading…</div>;
  }
}

function Card({
  title,
  children,
}: {
  title: string;
  children: ReactNode;
}) {
  return (
    <div className="flex min-h-screen items-start justify-center px-4 pt-[12vh]">
      <div className="w-full max-w-sm space-y-4 rounded-lg border border-slate-700 bg-slate-900 p-5">
        <span className="flex items-center gap-2 text-lg font-bold tracking-tight">
          <i className="inline-block h-5 w-5 rounded-[50%_50%_50%_4px] bg-sky-400" />
          LP Dashboard
        </span>
        <h1 className="text-lg font-semibold">{title}</h1>
        {children}
      </div>
    </div>
  );
}

function Alert({
  tone,
  children,
}: {
  tone: "error" | "warn";
  children: ReactNode;
}) {
  const colors =
    tone === "error"
      ? "border-rose-500/35 bg-rose-500/10 text-rose-400"
      : "border-amber-500/35 bg-amber-500/10 text-amber-300";
  return (
    <div role="alert" className={`rounded-md border px-3 py-2 text-sm ${colors}`}>
      {children}
    </div>
  );
}

/** Password managers want a username next to the password; the site has none, so give them a fixed hidden one. */
function UsernameForPasswordManagers() {
  return (
    <input
      type="text"
      name="username"
      autoComplete="username"
      defaultValue="lp-dashboard"
      readOnly
      tabIndex={-1}
      aria-hidden="true"
      className="sr-only"
    />
  );
}

function describeLoginError(e: unknown): { tone: "error" | "warn"; text: string } {
  if (e instanceof ApiError) {
    if (e.code === "wrong_password")
      return { tone: "error", text: "Wrong password. Check it and try again." };
    if (e.code === "throttled")
      return { tone: "warn", text: `Too many wrong attempts. ${e.message}` };
    return { tone: "error", text: e.message };
  }
  return { tone: "error", text: "Could not reach the server. Try again." };
}

function LoginScreen() {
  const { login } = useAuth();
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<ReturnType<typeof describeLoginError> | null>(null);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setProblem(null);
    try {
      await login(password);
    } catch (err) {
      setProblem(describeLoginError(err));
      setBusy(false);
    }
  };

  return (
    <Card title="Sign in">
      <form onSubmit={submit} className="space-y-4">
        <UsernameForPasswordManagers />
        <label className="block space-y-1 text-xs font-medium text-slate-300">
          Password
          <input
            id="login-password"
            type="password"
            autoFocus
            autoComplete="current-password"
            className={`${inputClass} ${problem?.tone === "error" ? "border-rose-500" : ""}`}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
          />
        </label>
        {problem && <Alert tone={problem.tone}>{problem.text}</Alert>}
        <button className={`${buttonClass} w-full`} disabled={busy || !password}>
          {busy ? "Signing in…" : "Sign in"}
        </button>
        <p className="text-xs text-slate-500">
          This device stays signed in until you log out.
        </p>
      </form>
    </Card>
  );
}

function CreatePasswordScreen() {
  const { setup } = useAuth();
  const [password, setPassword] = useState("");
  const [repeat, setRepeat] = useState("");
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (password.length < MIN_PASSWORD_LENGTH) {
      setProblem(`Use at least ${MIN_PASSWORD_LENGTH} characters.`);
      return;
    }
    if (password !== repeat) {
      setProblem("The two passwords do not match.");
      return;
    }
    setBusy(true);
    setProblem(null);
    try {
      await setup(password);
    } catch (err) {
      setProblem(
        err instanceof ApiError ? err.message : "Could not reach the server. Try again.",
      );
      setBusy(false);
    }
  };

  return (
    <Card title="Set your password">
      <form onSubmit={submit} className="space-y-4">
        <UsernameForPasswordManagers />
        <p className="text-sm text-slate-400">
          This is the only password protecting the site. Wallets and loans
          already on this server become yours.
        </p>
        <label className="block space-y-1 text-xs font-medium text-slate-300">
          New password
          <input
            id="setup-password"
            type="password"
            autoFocus
            autoComplete="new-password"
            className={inputClass}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
          />
          <span className="block text-xs font-normal text-slate-500">
            At least {MIN_PASSWORD_LENGTH} characters.
          </span>
        </label>
        <label className="block space-y-1 text-xs font-medium text-slate-300">
          Repeat password
          <input
            id="setup-password-repeat"
            type="password"
            autoComplete="new-password"
            className={inputClass}
            value={repeat}
            onChange={(e) => setRepeat(e.target.value)}
            required
          />
        </label>
        {problem && <Alert tone="error">{problem}</Alert>}
        <button className={`${buttonClass} w-full`} disabled={busy}>
          {busy ? "Saving…" : "Set password and sign in"}
        </button>
      </form>
    </Card>
  );
}
