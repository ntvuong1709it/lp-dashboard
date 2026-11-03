import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BrowserRouter, Route, Routes } from "react-router-dom";
import "./index.css";
import "./lib/theme";
import { Layout } from "./components/Layout";
import { OwnerProvider } from "./lib/owner";
import { OverviewPage } from "./pages/OverviewPage";
import { ActivityPage } from "./pages/ActivityPage";
import { PnlPage } from "./pages/PnlPage";
import { PositionsPage } from "./pages/PositionsPage";
import { PositionDetailPage } from "./pages/PositionDetailPage";
import { FeeClaimsPage } from "./pages/FeeClaimsPage";
import { AddressesPage } from "./pages/AddressesPage";

const queryClient = new QueryClient({
  defaultOptions: { queries: { staleTime: 30_000, retry: 1 } },
});

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <OwnerProvider>
        <BrowserRouter>
          <Routes>
            <Route element={<Layout />}>
              <Route index element={<OverviewPage />} />
              <Route path="positions" element={<PositionsPage />} />
              <Route path="activity" element={<ActivityPage />} />
              <Route path="pnl" element={<PnlPage />} />
              <Route path="positions/:id" element={<PositionDetailPage />} />
              <Route path="fees" element={<FeeClaimsPage />} />
              <Route path="addresses" element={<AddressesPage />} />
            </Route>
          </Routes>
        </BrowserRouter>
      </OwnerProvider>
    </QueryClientProvider>
  </StrictMode>,
);
