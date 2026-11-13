/**
 * The archive rule for anything dated on the website: once its end time has passed (Johannesburg time is in the
 * ISO string), it stops being promoted. Booking buttons go, "coming up" wording goes, and it moves to past
 * highlights or off the list. The site rebuilds every hour; scripts/archive.ts applies the same rule in the
 * browser so a page built before an event ended never shows it as upcoming.
 */
export const hasEnded = (endsAt: string, now = new Date()) => new Date(endsAt).getTime() <= now.getTime();
