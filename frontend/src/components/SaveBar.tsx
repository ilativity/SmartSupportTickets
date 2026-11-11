import { Button } from './ui'

/**
 * Appears only while there are unsaved inline edits. Fixed to the bottom of the screen, so on a phone the Save
 * button is reachable without scrolling back through the cards.
 */
export function SaveBar({
  count,
  hidden,
  busy,
  onSave,
  onDiscard,
}: {
  count: number
  /** Edited rows the current filter hides: kept, but not saved until they are visible again. */
  hidden: number
  busy: boolean
  onSave: () => void
  onDiscard: () => void
}) {
  if (count === 0 && hidden === 0) return null
  return (
    <div role="region" aria-label="Unsaved changes" className="fixed inset-x-0 bottom-0 z-40 border-t border-slate-200 bg-white/95 shadow-[0_-4px_12px_rgba(15,23,42,0.08)] backdrop-blur">
      <div className="mx-auto flex max-w-6xl items-center justify-between gap-3 px-4 py-3">
        <p className="text-sm font-medium text-slate-700">
          {count} unsaved {count === 1 ? 'change' : 'changes'}
          {hidden > 0 && <span className="block text-xs font-normal text-slate-500">{hidden} more hidden by the filter (not saved)</span>}
        </p>
        <div className="flex gap-2">
          <Button variant="secondary" onClick={onDiscard} disabled={busy}>
            Discard
          </Button>
          <Button onClick={onSave} busy={busy} disabled={count === 0}>
            Save changes
          </Button>
        </div>
      </div>
    </div>
  )
}
