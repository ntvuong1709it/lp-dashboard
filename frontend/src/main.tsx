import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BrowserRouter, Route, Routes } from "react-router-dom";
import "./index.css";
import "./lib/theme";
import { AuthGate } from "./components/AuthGate";
import { Layout } from "./components/Layout";
import { AuthProvider } from "./lib/auth";
import { OwnerProvider } from "./lib/owner";
import { OverviewPage } from "./pages/OverviewPage";
import { ActivityPage } from "./pages/ActivityPage";
import { PnlPage } from "./pages/PnlPage";
import { PositionsPage } from "./pages/PositionsPage";
import { PositionDetailPage } from "./pages/PositionDetailPage";
import { FeeClaimsPage } from "./pages/FeeClaimsPage";
import { AddressesPage } from "./pages/AddressesPage";
import { PoolsPage } from "./pages/PoolsPage";
import { BorrowPage } from "./pages/BorrowPage";

const queryClient = new QueryClient({
  defaultOptions: { queries: { staleTime: 30_000, retry: 1 } },
});

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <AuthGate>
          <OwnerProvider>
            <BrowserRouter>
              <Routes>
                <Route element={<Layout />}>
                  <Route index element={<OverviewPage />} />
                  <Route path="positions" element={<PositionsPage />} />
                  <Route path="activity" element={<ActivityPage />} />
                  <Route path="pnl" element={<PnlPage />} />
                  <Route path="positions/:id" element={<PositionDetailPage />} />
                  <Route path="pools" element={<PoolsPage />} />
                  <Route path="fees" element={<FeeClaimsPage />} />
                  <Route path="borrow" element={<BorrowPage />} />
                  <Route path="addresses" element={<AddressesPage />} />
                </Route>
              </Routes>
            </BrowserRouter>
          </OwnerProvider>
        </AuthGate>
      </AuthProvider>
    </QueryClientProvider>
  </StrictMode>,
);
