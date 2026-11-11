import { Link } from 'react-router'
import type { TicketListItem } from '../api/types'
import { formatDate } from '../lib/format'
import { StatusBadge, TicketRef } from './ui'

// Two layouts for the same data: a table from the md breakpoint up (as in the mockup) and cards on phones, where a
// six-column table would force sideways scrolling.

export function TicketTable({ tickets }: { tickets: TicketListItem[] }) {
  return (
    <div className="hidden overflow-hidden rounded-xl border border-slate-200 bg-white md:block">
      <table className="w-full table-fixed break-words text-left text-sm">
        <thead className="border-b border-slate-200 bg-slate-50 text-xs font-semibold uppercase tracking-wide text-slate-600">
          <tr>
            <th scope="col" className="w-[16%] px-4 py-3">Name</th>
            <th scope="col" className="w-[22%] px-4 py-3">Summary</th>
            <th scope="col" className="px-4 py-3">Description</th>
            <th scope="col" className="w-[12%] px-4 py-3">Status</th>
            <th scope="col" className="w-[17%] px-4 py-3">Resolution</th>
            <th scope="col" className="w-[15%] px-4 py-3">Updated</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-100">
          {tickets.map((t) => (
            // "Stretched link": the name is a real <a>, and its ::after covers the whole row, so the row is
            // clickable while keyboard focus, middle-click and "open in new tab" still work as for any link.
            <tr key={t.id} className="relative align-top hover:bg-brand-50/60">
              <td className="px-4 py-3">
                <Link
                  to={`/tickets/${t.id}`}
                  className="font-medium text-slate-900 after:absolute after:inset-0 after:content-[''] hover:text-brand-700"
                >
                  {t.name}
                </Link>
                <div><TicketRef id={t.id} /></div>
              </td>
              <td className="px-4 py-3 text-slate-700">{t.summary ?? <span className="text-slate-400">—</span>}</td>
              <td className="px-4 py-3 text-slate-600">
                <p className="line-clamp-2" title={t.description}>{t.description}</p>
              </td>
              <td className="px-4 py-3"><StatusBadge status={t.status} /></td>
              <td className="px-4 py-3 text-slate-600">
                <p className="line-clamp-2">{t.resolution || <span className="text-slate-400">—</span>}</p>
              </td>
              <td className="px-4 py-3 text-xs text-slate-500">{formatDate(t.updatedAt)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

export function TicketCards({ tickets }: { tickets: TicketListItem[] }) {
  return (
    <ul className="space-y-3 md:hidden">
      {tickets.map((t) => (
        <li key={t.id}>
          <Link
            to={`/tickets/${t.id}`}
            className="block rounded-xl border border-slate-200 bg-white p-4 shadow-sm active:bg-brand-50"
          >
            <div className="flex items-start justify-between gap-3">
              <div className="min-w-0">
                <p className="truncate font-medium text-slate-900">{t.name}</p>
                <TicketRef id={t.id} />
              </div>
              <StatusBadge status={t.status} />
            </div>
            <p className="mt-2 line-clamp-3 break-words text-sm text-slate-700">{t.summary ?? t.description}</p>
            {t.resolution && <p className="mt-2 line-clamp-2 break-words text-sm text-slate-500">Resolution: {t.resolution}</p>}
            <p className="mt-2 text-xs text-slate-500">Updated {formatDate(t.updatedAt)}</p>
          </Link>
        </li>
      ))}
    </ul>
  )
}
