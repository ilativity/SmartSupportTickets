import { Link } from 'react-router'
import type { TicketListItem } from '../api/types'
import { formatDate } from '../lib/format'
import { StatusBadge, TicketRef, inputClass } from './ui'

// Two layouts for the same data: a table from the md breakpoint up (as in the mockup) and cards on phones, where a
// six-column table would force sideways scrolling. Both offer the same inline editing to an admin (decision D16).

/** The fields an admin has touched in one row, exactly as typed (trimming happens only when saving). */
export interface RowEdit {
  status?: string
  resolution?: string
}

/** Present only for an admin: the list becomes editable. */
export interface InlineEditing {
  statuses: string[]
  edits: Record<string, RowEdit>
  errors: Record<string, string>
  busy: boolean
  onChange: (id: string, field: keyof RowEdit, value: string) => void
}

/** True if the edit differs from what the server has now. Computed, never stored, so it cannot go stale. */
export function isChanged(ticket: TicketListItem, edit: RowEdit | undefined): boolean {
  if (!edit) return false
  return (
    (edit.status !== undefined && edit.status !== ticket.status) ||
    (edit.resolution !== undefined && edit.resolution.trim() !== ticket.resolution)
  )
}

/** "Status of Jane Smith's ticket #9a8b4c" — the short id keeps labels unique when two customers share a name. */
const labelFor = (what: string, t: TicketListItem) => `${what} of ${t.name}'s ticket #${t.id.slice(0, 6)}`

function StatusSelect({ ticket, editing, id, labelled }: { ticket: TicketListItem; editing: InlineEditing; id: string; labelled?: boolean }) {
  const value = editing.edits[ticket.id]?.status ?? ticket.status
  // The current value is always an option, even before the status list has loaded.
  const options = editing.statuses.includes(value) ? editing.statuses : [value, ...editing.statuses]
  return (
    <select
      id={id}
      aria-label={labelled ? undefined : labelFor('Status', ticket)}
      className={`${inputClass} relative z-10`}
      value={value}
      disabled={editing.busy}
      onChange={(e) => editing.onChange(ticket.id, 'status', e.target.value)}
    >
      {options.map((s) => (
        <option key={s} value={s}>{s}</option>
      ))}
    </select>
  )
}

function ResolutionInput({ ticket, editing, id, labelled }: { ticket: TicketListItem; editing: InlineEditing; id: string; labelled?: boolean }) {
  const value = editing.edits[ticket.id]?.resolution ?? ticket.resolution
  const error = editing.errors[ticket.id]
  return (
    <textarea
      id={id}
      aria-label={labelled ? undefined : labelFor('Resolution', ticket)}
      aria-invalid={!!error || undefined}
      aria-describedby={error ? `row-error-${ticket.id}` : undefined}
      rows={2}
      maxLength={2000}
      className={`${inputClass} relative z-10`}
      value={value}
      disabled={editing.busy}
      onChange={(e) => editing.onChange(ticket.id, 'resolution', e.target.value)}
    />
  )
}

function RowError({ ticket, editing }: { ticket: TicketListItem; editing?: InlineEditing }) {
  const message = editing?.errors[ticket.id]
  return message ? (
    <p id={`row-error-${ticket.id}`} role="alert" className="relative z-10 mt-1 text-xs text-red-700">
      {message}
    </p>
  ) : null
}

export function TicketTable({ tickets, editing }: { tickets: TicketListItem[]; editing?: InlineEditing }) {
  return (
    <div className="hidden overflow-hidden rounded-xl border border-slate-200 bg-white md:block">
      <table className="w-full table-fixed break-words text-left text-sm">
        <thead className="border-b border-slate-200 bg-slate-50 text-xs font-semibold uppercase tracking-wide text-slate-600">
          <tr>
            <th scope="col" className="w-[16%] px-4 py-3">Name</th>
            <th scope="col" className="w-[20%] px-4 py-3">Summary</th>
            <th scope="col" className="px-4 py-3">Description</th>
            <th scope="col" className={editing ? 'w-[15%] px-4 py-3' : 'w-[12%] px-4 py-3'}>Status</th>
            <th scope="col" className={editing ? 'w-[22%] px-4 py-3' : 'w-[17%] px-4 py-3'}>Resolution</th>
            <th scope="col" className="w-[13%] px-4 py-3">Updated</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-100">
          {tickets.map((t) => {
            const dirty = !!editing && isChanged(t, editing.edits[t.id])
            return (
              // "Stretched link": the name is a real <a>, and its ::after covers the whole row, so the row is clickable
              // while keyboard focus, middle-click and "open in new tab" still work. Edit controls sit above it (z-10).
              <tr key={t.id} className={`relative align-top ${dirty ? 'bg-amber-50' : 'hover:bg-brand-50/60'}`}>
                <td className="px-4 py-3">
                  <Link
                    to={`/tickets/${t.id}`}
                    className="font-medium text-slate-900 after:absolute after:inset-0 after:content-[''] hover:text-brand-700"
                  >
                    {t.name}
                  </Link>
                  <div><TicketRef id={t.id} /></div>
                  {dirty && <span className="text-xs font-medium text-amber-800">Unsaved</span>}
                </td>
                <td className="px-4 py-3 text-slate-700">{t.summary ?? <span className="text-slate-400">—</span>}</td>
                <td className="px-4 py-3 text-slate-600">
                  <p className="line-clamp-2" title={t.description}>{t.description}</p>
                </td>
                <td className="px-4 py-3">
                  {editing ? <StatusSelect ticket={t} editing={editing} id={`status-${t.id}`} /> : <StatusBadge status={t.status} />}
                </td>
                <td className="px-4 py-3 text-slate-600">
                  {editing ? (
                    <>
                      <ResolutionInput ticket={t} editing={editing} id={`resolution-${t.id}`} />
                      <RowError ticket={t} editing={editing} />
                    </>
                  ) : (
                    <p className="line-clamp-2">{t.resolution || <span className="text-slate-400">—</span>}</p>
                  )}
                </td>
                <td className="px-4 py-3 text-xs text-slate-500">{formatDate(t.updatedAt)}</td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}

export function TicketCards({ tickets, editing }: { tickets: TicketListItem[]; editing?: InlineEditing }) {
  return (
    <ul className="space-y-3 md:hidden">
      {tickets.map((t) => {
        const body = (
          <>
            <div className="flex items-start justify-between gap-3">
              <div className="min-w-0">
                <p className="truncate font-medium text-slate-900">{t.name}</p>
                <TicketRef id={t.id} />
              </div>
              {!editing && <StatusBadge status={t.status} />}
            </div>
            <p className="mt-2 line-clamp-3 break-words text-sm text-slate-700">{t.summary ?? t.description}</p>
            {!editing && t.resolution && (
              <p className="mt-2 line-clamp-2 break-words text-sm text-slate-500">Resolution: {t.resolution}</p>
            )}
            <p className="mt-2 text-xs text-slate-500">Updated {formatDate(t.updatedAt)}</p>
          </>
        )

        if (!editing) {
          // For visitors the whole card is one link.
          return (
            <li key={t.id}>
              <Link to={`/tickets/${t.id}`} className="block rounded-xl border border-slate-200 bg-white p-4 shadow-sm active:bg-brand-50">
                {body}
              </Link>
            </li>
          )
        }

        // For an admin the card holds form controls, which may not sit inside a link: a separate link opens the ticket.
        const dirty = isChanged(t, editing.edits[t.id])
        const labelClass = 'block text-xs font-semibold uppercase tracking-wide text-slate-500'
        return (
          <li key={t.id} className={`rounded-xl border p-4 shadow-sm ${dirty ? 'border-amber-300 bg-amber-50' : 'border-slate-200 bg-white'}`}>
            {body}
            <Link to={`/tickets/${t.id}`} className="mt-2 inline-block text-sm font-semibold text-brand-700 underline underline-offset-2">
              Open ticket<span className="sr-only"> of {t.name}</span>
            </Link>
            <div className="mt-3 space-y-2">
              <label htmlFor={`card-status-${t.id}`} className={labelClass}>Status</label>
              <StatusSelect ticket={t} editing={editing} id={`card-status-${t.id}`} labelled />
              <label htmlFor={`card-resolution-${t.id}`} className={labelClass}>Resolution</label>
              <ResolutionInput ticket={t} editing={editing} id={`card-resolution-${t.id}`} labelled />
              <RowError ticket={t} editing={editing} />
              {dirty && <p className="text-xs font-medium text-amber-800">Unsaved</p>}
            </div>
          </li>
        )
      })}
    </ul>
  )
}
