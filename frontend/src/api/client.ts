// The only place in the frontend that calls fetch. Pages and components call the typed functions in tickets.ts and
// auth.ts and never see URLs, headers or status codes — the frontend's side of the adapter boundary (decision D12).

const TOKEN_KEY = 'sst.token'

/**
 * The admin token lives in sessionStorage: it survives a reload but not closing the tab, and it is not sent by the
 * browser on its own (unlike a cookie), which is why the API needs no CSRF protection.
 */
export const tokenStore = {
  get: (): string | null => safe(() => sessionStorage.getItem(TOKEN_KEY)),
  set: (token: string) => safe(() => sessionStorage.setItem(TOKEN_KEY, token)),
  clear: () => safe(() => sessionStorage.removeItem(TOKEN_KEY)),
}

// Storage can throw (private mode, blocked site data); the app must still work, just without remembering the login.
function safe<T>(action: () => T): T | null {
  try {
    return action()
  } catch {
    return null
  }
}

/** Called when the API answers 401 to a request that carried a token: the token expired or was revoked. */
let onUnauthorized: () => void = () => {}
export function setUnauthorizedHandler(handler: () => void) {
  onUnauthorized = handler
}

/** What the UI needs to know about a failed request. Built from the API's RFC 7807 ProblemDetails body. */
export class ApiError extends Error {
  readonly status: number
  /** Per-field validation messages, keyed by field name ("name", "email", ...). */
  readonly fieldErrors: Record<string, string[]>

  constructor(status: number, message: string, fieldErrors: Record<string, string[]> = {}) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.fieldErrors = fieldErrors
  }

  get isRateLimited() {
    return this.status === 429
  }

  get isNotFound() {
    return this.status === 404
  }

  /** The request never got an HTTP answer (offline, server down). */
  get isNetwork() {
    return this.status === 0
  }
}

interface ProblemDetails {
  title?: string
  detail?: string
  errors?: Record<string, string[]>
}

export async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers)
  const token = tokenStore.get()
  if (token) {
    headers.set('Authorization', `Bearer ${token}`)
  }
  headers.set('Accept', 'application/json')

  let response: Response
  try {
    response = await fetch(path, { ...init, headers })
  } catch (error) {
    // A cancelled request (filter changed, page left) is not a connection problem; let the caller see it as such.
    if (isAbort(error)) throw error
    throw new ApiError(0, 'Cannot reach the server. Check your connection and try again.')
  }

  if (response.ok) {
    if (response.status === 204) return undefined as T
    try {
      return (await response.json()) as T
    } catch (error) {
      if (isAbort(error)) throw error
      // e.g. a proxy or the SPA fallback answered an /api path with an HTML page
      throw new ApiError(response.status, 'Unexpected response from the server.')
    }
  }

  const problem = await readProblem(response)
  // Only if the token that failed is still the current one: a late 401 for an old session must not log out a new one.
  if (response.status === 401 && token && tokenStore.get() === token) {
    onUnauthorized()
  }

  throw new ApiError(response.status, messageFor(response.status, problem), problem.errors ?? {})
}

async function readProblem(response: Response): Promise<ProblemDetails> {
  try {
    return (await response.json()) as ProblemDetails
  } catch (error) {
    if (isAbort(error)) throw error
    return {} // e.g. an HTML error page from a proxy
  }
}

function isAbort(error: unknown) {
  return error instanceof DOMException && error.name === 'AbortError'
}

function messageFor(status: number, problem: ProblemDetails): string {
  // 429 has its own message: showing "wrong password" for a rate limit would send the user guessing again.
  if (status === 429) return 'Too many attempts. Please wait a minute and try again.'
  if (problem.detail) return problem.detail
  if (status === 401) return 'Please log in.'
  if (status === 403) return 'You are not allowed to do that.'
  if (status === 404) return 'Not found.'
  if (status === 413) return 'The file is too large.'
  if (status >= 500) return 'Something went wrong on the server. Please try again.'
  return problem.title ?? 'The request failed.'
}
