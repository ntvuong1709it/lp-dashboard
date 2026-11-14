import { Navigate, Outlet, useLocation } from "react-router-dom";
import { useHealth } from "../api/client";
import { Header } from "./header/Header";
import { PositionDrawer } from "./PositionDrawer";
import { SetupScreen } from "./SetupScreen";
import { useOwner } from "../lib/owner";

export function Layout() {
  // The pool explorer is a table next to a side panel, so it gets more of a wide screen than the other pages.
  const { pathname } = useLocation();
  const wide = pathname.startsWith("/pools");
  const width = wide ? "max-w-[110rem]" : "max-w-7xl";
  return (
    <div className="min-h-screen">
      <Header width={width} />
      <main className={`mx-auto ${width} px-4 py-6`}>
        <Content />
      </main>
      <PositionDrawer />
    </div>
  );
}

/** The routed page, or the setup screen when no chain provider is configured (wallets can still be managed). */
function Content() {
  const { data: health } = useHealth();
  const { pathname } = useLocation();
  const { addresses } = useOwner();
  const needsSetup = health?.providers.length === 0;

  // A brand-new account has no wallets yet, so it starts by entering one.
  if (addresses.length === 0 && pathname !== "/addresses")
    return <Navigate to="/addresses" replace />;
  if (needsSetup && pathname !== "/addresses") return <SetupScreen />;
  return <Outlet />;
}
