import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import { getStatuses, listTickets } from '../api/tickets'
import { NewTicketModal } from '../components/NewTicketModal'
import { TicketCards, TicketTable } from '../components/TicketList'
import { useToast } from '../components/Toast'
import { Button, EmptyState, ErrorState, Spinner, inputClass } from '../components/ui'
import { useDebouncedValue } from '../hooks/useDebouncedValue'

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
          <TicketTable tickets={tickets.data} />
          <TicketCards tickets={tickets.data} />
        </div>
      )}

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
