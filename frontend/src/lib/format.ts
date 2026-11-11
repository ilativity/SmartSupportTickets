const dateFormat = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' })

/** A date in the reader's own locale and time zone, e.g. "Oct 27, 2025, 4:35 PM". */
export function formatDate(iso: string) {
  return dateFormat.format(new Date(iso))
}
