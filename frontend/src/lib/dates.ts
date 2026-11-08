/** Chart tooltip labels that include the weekday ("Mon, Nov 3"), so a day can be read as a Monday or a Tuesday. */

/** A calendar day given as YYYY-MM-DD, shown as "Mon, Nov 3" (the day itself, not shifted by the browser's zone). */
export const weekdayDate = (iso: string) =>
  new Date(`${iso}T00:00:00Z`).toLocaleDateString(undefined, {
    weekday: "short",
    month: "short",
    day: "numeric",
    timeZone: "UTC",
  });

/** A moment in time (ms since epoch), shown in the browser's zone as a date: "Mon, Nov 3". */
export const weekdayOf = (ms: number) =>
  new Date(ms).toLocaleDateString(undefined, { weekday: "short", month: "short", day: "numeric" });

/** A moment in time, shown in the browser's zone with the weekday: "Mon, Nov 3, 2026, 2:30 PM". */
export const weekdayDateTime = (ms: number) =>
  new Date(ms).toLocaleString(undefined, {
    weekday: "short",
    year: "numeric",
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
  });
