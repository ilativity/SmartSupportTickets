import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import { ApiError } from '../api/client'
import { getStatuses, listTickets, updateTicket } from '../api/tickets'
import { useAuth } from '../auth/AuthContext'
import { NewTicketModal } from '../components/NewTicketModal'
import { SaveBar } from '../components/SaveBar'
import { TicketCards, TicketTable, isChanged, type InlineEditing, type RowEdit } from '../components/TicketList'
import { useToast } from '../components/Toast'
import { Button, EmptyState, ErrorState, Spinner, inputClass } from '../components/ui'
import { useDebouncedValue } from '../hooks/useDebouncedValue'
import { useInflightGuard } from '../hooks/useInflightGuard'

export function TicketsPage() {
  // Filters live in the URL (?status=…&search=…): a filtered list can be bookmarked, shared and survives a reload.
  // The URL is the single source of truth for what is listed; the search box is only an editor for it.
  const [params, setParams] = useSearchParams()
  const status = params.get('status') ?? ''
  const urlSearch = params.get('search') ?? ''
  const [searchInput, setSearchInput] = useState(urlSearch)
  const debouncedInput = useDebouncedValue(searchInput, 300)
  const lastWritten = useRef(urlSearch) // the search value this page itself last put in the URL
  const [creating, setCreating] = useState(false)
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const toast = useToast()
  const { admin } = useAuth()

  // Inline editing (admin only, decision D16). Only the fields the admin touched are kept, exactly as typed; whether
  // a row counts as changed is computed against the latest server values on every render, never stored.
  const [edits, setEdits] = useState<Record<string, RowEdit>>({})
  const [editErrors, setEditErrors] = useState<Record<string, string>>({})
  const saving = useInflightGuard()

  // Logging out drops unsaved edits: they could no longer be saved anyway.
  useEffect(() => {
    if (!admin) {
      setEdits({})
      setEditErrors({})
    }
  }, [admin])

  // URL changed by navigation (Back/Forward, a link): show that value in the box.
  useEffect(() => {
    if (urlSearch !== lastWritten.current) {
      lastWritten.current = urlSearch
      setSearchInput(urlSearch)
    }
  }, [urlSearch])

  // Typing (after the debounce): write the value to the URL, replacing the entry so typing does not fill history.
  useEffect(() => {
    const value = debouncedInput.trim()
    if (value === lastWritten.current) return
    lastWritten.current = value
    setParams(
      (current) => {
        const next = new URLSearchParams(current)
        if (value) next.set('search', value)
        else next.delete('search')
        return next
      },
      { replace: true },
    )
  }, [debouncedInput, setParams])

  const statuses = useQuery({ queryKey: ['statuses'], queryFn: getStatuses, staleTime: Infinity })
  const tickets = useQuery({
    queryKey: ['tickets', { status, search: urlSearch }],
    queryFn: ({ signal }) => listTickets({ status, search: urlSearch }, signal),
    placeholderData: keepPreviousData, // keep showing the old list while the new filter loads
  })

  function changeRow(id: string, field: keyof RowEdit, value: string) {
    setEdits((current) => ({ ...current, [id]: { ...current[id], [field]: value } }))
    setEditErrors(({ [id]: _cleared, ...rest }) => rest)
  }

  const visible = tickets.data ?? []
  const changed = visible.filter((t) => isChanged(t, edits[t.id]))
  // Edits in rows the current filter hides are kept (they reappear with the rows) but are never saved unseen.
  const hiddenEdits = Object.keys(edits).filter((id) => !visible.some((t) => t.id === id)).length

  /** One PUT per changed, visible ticket. Successes are cleared; failures stay marked with their reason. */
  async function saveAll() {
    await saving.run(async () => {
      const batch = changed.map((ticket) => {
        const sent = edits[ticket.id]
        // Untouched fields come from the row as it is now, so an edit to one field never resets the other.
        const body = { status: sent.status ?? ticket.status, resolution: (sent.resolution ?? ticket.resolution).trim() }
        return { id: ticket.id, sent, body }
      })
      const results = await Promise.allSettled(batch.map((b) => updateTicket(b.id, b.body)))

      const failed: Record<string, string> = {}
      results.forEach((result, i) => {
        if (result.status === 'rejected') {
          const error = result.reason
          failed[batch[i].id] = error instanceof ApiError ? (Object.values(error.fieldErrors)[0]?.[0] ?? error.message) : 'Could not save.'
        }
      })

      // Fresh rows first, then clear the edits: otherwise the old values flash back for a moment.
      await queryClient.invalidateQueries({ queryKey: ['tickets'] })
      void queryClient.invalidateQueries({ queryKey: ['ticket'] })
      setEdits((current) => {
        const next = { ...current }
        batch.forEach((b, i) => {
          // Only if nothing was typed in that row while the save was running (same object = untouched since sent).
          if (results[i].status === 'fulfilled' && next[b.id] === b.sent) delete next[b.id]
        })
        return next
      })
      setEditErrors(failed)

      const failures = Object.keys(failed).length
      const saved = batch.length - failures
      if (failures === 0) {
        toast(`Saved ${saved} ${saved === 1 ? 'ticket' : 'tickets'}. Customers were notified by email.`)
      } else {
        toast(`Saved ${saved}, ${failures} could not be saved — see the marked rows.`, { tone: 'error' })
      }
    })
  }

  const editing: InlineEditing | undefined = admin
    ? { statuses: statuses.data ?? [], edits, errors: editErrors, busy: saving.busy, onChange: changeRow }
    : undefined

  function setStatus(value: string) {
    setParams((current) => {
      const next = new URLSearchParams(current)
      if (value) next.set('status', value)
      else next.delete('status')
      return next
    })
  }

  return (
    <>
      <div className="mb-5 flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold text-slate-900">Support tickets</h1>
          <p className="text-sm text-slate-600">Every request and where it stands.</p>
        </div>
        <Button variant="accent" onClick={() => setCreating(true)}>
          <span aria-hidden="true">+</span> New ticket
        </Button>
      </div>

      <div className="mb-4 grid gap-3 sm:grid-cols-[minmax(0,14rem)_1fr]">
        <div>
          <label htmlFor="status-filter" className="sr-only">Status</label>
          <select id="status-filter" className={inputClass} value={status} onChange={(e) => setStatus(e.target.value)}>
            <option value="">All statuses</option>
            {statuses.data?.map((s) => (
              <option key={s} value={s}>{s}</option>
            ))}
          </select>
        </div>
        <div>
          <label htmlFor="search" className="sr-only">Search</label>
          <input
            id="search"
            type="search"
            className={inputClass}
            placeholder="Search by name or description…"
            value={searchInput}
            maxLength={100}
            onChange={(e) => setSearchInput(e.target.value)}
          />
        </div>
      </div>

      {tickets.isPending ? (
        <div role="status" className="flex justify-center py-16 text-brand-600">
          <Spinner className="size-8" />
          <span className="sr-only">Loading tickets…</span>
        </div>
      ) : tickets.isError ? (
        <ErrorState message={`Could not load tickets. ${tickets.error.message}`} onRetry={() => tickets.refetch()} />
      ) : tickets.data.length === 0 ? (
        <EmptyState>
          {status || urlSearch ? 'No tickets match these filters.' : 'No tickets yet. Create the first one.'}
        </EmptyState>
      ) : (
        <div className={tickets.isFetching ? 'opacity-70 transition-opacity' : undefined}>
          <p className="mb-2 text-sm text-slate-600" aria-live="polite">
            {tickets.data.length} {tickets.data.length === 1 ? 'ticket' : 'tickets'}
          </p>
          <TicketTable tickets={tickets.data} editing={editing} />
          <TicketCards tickets={tickets.data} editing={editing} />
        </div>
      )}

      {/* Room for the fixed Save bar, so it never covers the last row. */}
      {(changed.length > 0 || hiddenEdits > 0) && <div className="h-20" aria-hidden="true" />}
      <SaveBar
        count={changed.length}
        hidden={hiddenEdits}
        busy={saving.busy}
        onSave={saveAll}
        onDiscard={() => {
          setEdits({})
          setEditErrors({})
        }}
      />

      <NewTicketModal
        open={creating}
        onClose={() => setCreating(false)}
        onCreated={(ticket) => {
          setCreating(false)
          void queryClient.invalidateQueries({ queryKey: ['tickets'] })
          // Straight to the new ticket: the customer sees their AI summary and the page the email links to.
          navigate(`/tickets/${ticket.id}`)
          toast('Ticket created. We emailed you a link to this page.')
        }}
      />
    </>
  )
}
