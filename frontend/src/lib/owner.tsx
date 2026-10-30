import { createContext, useContext, useState, type ReactNode } from "react";

const STORAGE_KEY = "lpdash.owner";

interface OwnerContextValue {
  owner: string | null;
  setOwner: (owner: string | null) => void;
}

const OwnerContext = createContext<OwnerContextValue>({
  owner: null,
  setOwner: () => {},
});

/** Selected wallet filter; null means "all watched addresses". */
export function OwnerProvider({ children }: { children: ReactNode }) {
  const [owner, setOwnerState] = useState<string | null>(() =>
    localStorage.getItem(STORAGE_KEY),
  );

  const setOwner = (value: string | null) => {
    if (value) localStorage.setItem(STORAGE_KEY, value);
    else localStorage.removeItem(STORAGE_KEY);
    setOwnerState(value);
  };

  return (
    <OwnerContext.Provider value={{ owner, setOwner }}>
      {children}
    </OwnerContext.Provider>
  );
}

export const useOwner = () => useContext(OwnerContext);
