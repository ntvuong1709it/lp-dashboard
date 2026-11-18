import type { PremiumDto } from "../../api/types";
import { PREMIUM_CHIP, PREMIUM_SHORT, formatPremium } from "../../lib/premium";
import { Chip } from "../borrow/bits";

/** US buying against selling on one line, for the Overview: "US flow +0.06%, buying a bit". Coloured by direction; it never turns the line into a warning. */
export function PremiumChip({ premium }: { premium: PremiumDto }) {
  const { percent, zone } = premium.today;
  return (
    <span title="Coinbase's price against Binance's, in dollars. A clue about who is buying, not a forecast.">
      <Chip className={PREMIUM_CHIP[zone]}>
        US flow {formatPremium(percent)}, {PREMIUM_SHORT[zone]}
      </Chip>
    </span>
  );
}
