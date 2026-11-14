import { useHealth } from "../api/client";
import { Panel } from "./ui";

/** Shown instead of the data pages when no chain provider (RPC URL) is configured. */
export function SetupScreen() {
  const health = useHealth();

  return (
    <div className="mx-auto max-w-2xl space-y-4">
      <Panel title="Setup needed: no RPC URL configured">
        <div className="space-y-4 text-sm text-fg-2">
          <p>
            The dashboard reads positions straight from the chain, so it needs
            a Base RPC URL (for example an Alchemy endpoint). Nothing can sync
            until one is set.
          </p>

          <div>
            <div className="mb-1 font-medium text-fg-2">Docker</div>
            <p className="mb-2 text-muted">
              Add this line to the <code>.env</code> file next to{" "}
              <code>docker-compose.yml</code>, then restart:
            </p>
            <pre className="overflow-x-auto rounded-md border border-line bg-surface p-3 text-xs">
              {`BASE_RPC_URL=https://base-mainnet.g.alchemy.com/v2/<KEY>

docker compose up -d`}
            </pre>
          </div>

          <div>
            <div className="mb-1 font-medium text-fg-2">Local dev</div>
            <pre className="overflow-x-auto rounded-md border border-line bg-surface p-3 text-xs">
              {`cd backend
dotnet user-secrets set "Evm:Chains:0:RpcUrl" "https://base-mainnet.g.alchemy.com/v2/<KEY>" --project src/LpDashboard.Api`}
            </pre>
          </div>

          <p className="text-xs text-faint">
            Keep the key out of git and out of screenshots. The setup is read
            when the app starts, so restart it after changing the value.
          </p>

          <button
            className="rounded-md border border-line-strong px-3 py-1.5 text-sm hover:bg-raised disabled:opacity-50"
            disabled={health.isFetching}
            onClick={() => void health.refetch()}
          >
            {health.isFetching ? "Checking…" : "Check again"}
          </button>
        </div>
      </Panel>
    </div>
  );
}
