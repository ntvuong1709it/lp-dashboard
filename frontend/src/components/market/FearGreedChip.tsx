import type { FearGreedDto } from "../../api/types";
import { ZONE_CHIP, ZONE_LABEL } from "../../lib/fearGreed";
import { Chip } from "../borrow/bits";

/** The index on one line, for the Overview: "Crypto mood 11, extreme fear". Coloured by zone; it never turns the line into a warning. */
export function FearGreedChip({ fearGreed }: { fearGreed: FearGreedDto }) {
  const { value, zone } = fearGreed.today;
  return (
    <span title="Crypto fear and greed index. A mood reading, not a forecast.">
      <Chip className={ZONE_CHIP[zone]}>
        Crypto mood {value}, {ZONE_LABEL[zone].toLowerCase()}
      </Chip>
    </span>
  );
}
