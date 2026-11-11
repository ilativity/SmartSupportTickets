// The HTTP contract, mirroring backend/SupportTickets.Api/Dtos. Dates arrive as ISO strings.

/** A status's display name, as served by GET /api/tickets/statuses ("New", "In Progress", ...). */
export type TicketStatus = string

/** A row of the public list — deliberately without the customer's email (privacy, decision D4). */
export interface TicketListItem {
  id: string
  name: string
  description: string
  summary: string | null
  status: TicketStatus
  resolution: string
  hasImage: boolean
  createdAt: string
  updatedAt: string
}

/** One ticket in full. `email` is masked (j***@example.com) unless the caller is an admin. */
export interface TicketDetails {
  id: string
  name: string
  email: string
  description: string
  summary: string | null
  imageUrl: string | null
  status: TicketStatus
  resolution: string
  createdAt: string
  updatedAt: string
}

export interface NewTicket {
  name: string
  email: string
  description: string
  image?: File | null
}

export interface TicketUpdate {
  status: TicketStatus
  resolution: string
}

export interface LoginResponse {
  token: string
  expiresAt: string
  username: string
}
