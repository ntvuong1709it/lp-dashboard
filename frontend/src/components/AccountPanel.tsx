import { useState, type FormEvent } from "react";
import { ApiError } from "../api/http";
import { MIN_PASSWORD_LENGTH, useAuth } from "../lib/auth";
import { ErrorText, Panel } from "./ui";

const inputClass =
  "w-full rounded-md border border-control bg-surface px-3 py-2 text-sm";

/** Change the password, or sign every device out (for example after losing one). */
export function AccountPanel() {
  const { changePassword, logoutEverywhere } = useAuth();
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [changed, setChanged] = useState(false);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setChanged(false);
    setError(null);
    if (next.length < MIN_PASSWORD_LENGTH) {
      setError(new Error(`Use at least ${MIN_PASSWORD_LENGTH} characters.`));
      return;
    }
    setBusy(true);
    try {
      await changePassword(current, next);
      setCurrent("");
      setNext("");
      setChanged(true);
    } catch (err) {
      setError(
        err instanceof ApiError && err.code === "wrong_password"
          ? new Error("The current password is wrong.")
          : err,
      );
    } finally {
      setBusy(false);
    }
  };

  const signOutEverywhere = async () => {
    if (
      !confirm(
        "Sign out every device, including this one? You will need your password to get back in.",
      )
    )
      return;
    try {
      await logoutEverywhere();
    } catch (err) {
      setError(err);
    }
  };

  return (
    <Panel title="Account">
      <form onSubmit={submit} className="space-y-3">
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
        <div className="grid gap-3 sm:grid-cols-2">
          <label className="block space-y-1 text-xs font-medium text-fg-2">
            Current password
            <input
              id="account-current-password"
              type="password"
              autoComplete="current-password"
              className={inputClass}
              value={current}
              onChange={(e) => setCurrent(e.target.value)}
              required
            />
          </label>
          <label className="block space-y-1 text-xs font-medium text-fg-2">
            New password
            <input
              id="account-new-password"
              type="password"
              autoComplete="new-password"
              placeholder={`At least ${MIN_PASSWORD_LENGTH} characters`}
              className={inputClass}
              value={next}
              onChange={(e) => setNext(e.target.value)}
              required
            />
          </label>
        </div>
        <div className="flex flex-wrap items-center gap-3">
          <button
            className="rounded-md bg-accent-solid px-3 py-1.5 text-sm font-medium text-white hover:bg-accent-fill disabled:opacity-50"
            disabled={busy || !current || !next}
          >
            {busy ? "Saving…" : "Change password"}
          </button>
          <span className="text-xs text-faint">
            Other devices are signed out when you change it.
          </span>
        </div>
        {changed && (
          <div role="status" className="text-sm text-up">
            Password changed.
          </div>
        )}
        {error != null && <ErrorText error={error} />}
      </form>
      <div className="mt-4 flex flex-wrap items-center gap-3 border-t border-line pt-3">
        <button
          className="rounded-md border border-line-strong px-3 py-1.5 text-sm text-down hover:bg-raised"
          onClick={() => void signOutEverywhere()}
        >
          Log out everywhere
        </button>
        <span className="text-xs text-faint">Use this if a device is lost.</span>
      </div>
    </Panel>
  );
}
