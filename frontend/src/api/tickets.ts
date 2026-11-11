import { request } from './client'
import type { NewTicket, TicketDetails, TicketListItem, TicketStatus, TicketUpdate } from './types'

export interface TicketFilter {
  status: string // "" = all
  search: string
}

export function listTickets(filter: TicketFilter, signal?: AbortSignal): Promise<TicketListItem[]> {
  const query = new URLSearchParams()
  if (filter.status) query.set('status', filter.status)
  if (filter.search.trim()) query.set('search', filter.search.trim())
  const suffix = query.size ? `?${query}` : ''
  return request<TicketListItem[]>(`/api/tickets${suffix}`, { signal })
}

export function getStatuses(): Promise<TicketStatus[]> {
  return request<TicketStatus[]>('/api/tickets/statuses')
}

export function getTicket(id: string, signal?: AbortSignal): Promise<TicketDetails> {
  return request<TicketDetails>(`/api/tickets/${encodeURIComponent(id)}`, { signal })
}

/** multipart/form-data, so an image can travel with the text fields. The browser sets the boundary header. */
export function createTicket(ticket: NewTicket): Promise<TicketDetails> {
  const form = new FormData()
  form.set('name', ticket.name)
  form.set('email', ticket.email)
  form.set('description', ticket.description)
  if (ticket.image) form.set('image', ticket.image)
  return request<TicketDetails>('/api/tickets', { method: 'POST', body: form })
}

export function updateTicket(id: string, update: TicketUpdate): Promise<TicketDetails> {
  return request<TicketDetails>(`/api/tickets/${encodeURIComponent(id)}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(update),
  })
}
