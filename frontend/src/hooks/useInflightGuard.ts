import { useCallback, useRef, useState } from 'react'

/**
 * Runs an async action at most once at a time. A double click on Submit must not create two tickets.
 *
 * The guard is a ref, not state: a state update is applied on the next render, so two clicks inside the same frame
 * would both still see "not busy". A ref changes immediately. `busy` (state) is only for showing a spinner.
 */
export function useInflightGuard() {
  const running = useRef(false)
  const [busy, setBusy] = useState(false)

  const run = useCallback(async <T,>(action: () => Promise<T>): Promise<T | undefined> => {
    if (running.current) return undefined
    running.current = true
    setBusy(true)
    try {
      return await action()
    } finally {
      running.current = false
      setBusy(false)
    }
  }, [])

  return { run, busy }
}
