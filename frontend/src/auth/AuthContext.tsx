import { useQueryClient } from '@tanstack/react-query'
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { login as apiLogin } from '../api/auth'
import { setUnauthorizedHandler, tokenStore } from '../api/client'

interface AdminSession {
  username: string
  expiresAt: string
}

interface AuthState {
  admin: AdminSession | null
  login: (username: string, password: string) => Promise<void>
  logout: () => void
}

const SESSION_KEY = 'sst.admin'
const AuthContext = createContext<AuthState | null>(null)

function readSession(): AdminSession | null {
  try {
    const raw = sessionStorage.getItem(SESSION_KEY)
    const session = raw ? (JSON.parse(raw) as AdminSession) : null
    if (session && tokenStore.get() && new Date(session.expiresAt) > new Date()) {
      return session
    }
  } catch {
    // unreadable storage or JSON: treated as logged out
  }
  // An expired or partial session is no session; remove its token so it is not sent with anonymous requests.
  tokenStore.clear()
  try {
    sessionStorage.removeItem(SESSION_KEY)
  } catch {
    // storage unavailable
  }
  return null
}

// setTimeout fires at once for delays above 2^31 ms (~24.8 days); tokens live far shorter, but never rely on it.
const MAX_TIMER_MS = 2_147_483_647

/**
 * Who is logged in. The server decides what an admin may do — this only drives the UI (show edit controls or a
 * "Log in to edit" link). Being "admin" here grants nothing by itself: every edit carries the token the API checks.
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const [admin, setAdmin] = useState<AdminSession | null>(readSession)
  const queryClient = useQueryClient()

  const logout = useCallback(() => {
    tokenStore.clear()
    try {
      sessionStorage.removeItem(SESSION_KEY)
    } catch {
      // storage unavailable: nothing to remove
    }
    setAdmin(null)
    // Cached ticket details may contain full emails fetched as admin; drop them.
    queryClient.removeQueries({ queryKey: ['ticket'] })
  }, [queryClient])

  const login = useCallback(
    async (username: string, password: string) => {
      const response = await apiLogin(username, password)
      tokenStore.set(response.token)
      const session = { username: response.username, expiresAt: response.expiresAt }
      try {
        sessionStorage.setItem(SESSION_KEY, JSON.stringify(session))
      } catch {
        // storage unavailable: the login lasts until reload
      }
      setAdmin(session)
      // Details fetched before login show masked emails; fetch them again with the token.
      void queryClient.invalidateQueries({ queryKey: ['ticket'] })
    },
    [queryClient],
  )

  // The API answering 401 to a request with the current token means that token is no longer valid.
  useEffect(() => setUnauthorizedHandler(logout), [logout])

  // Belt and braces: whenever nobody is logged in, no admin-only data (full emails) stays in the cache — including
  // data written by a save that was still in flight when the user logged out.
  useEffect(() => {
    if (!admin) queryClient.removeQueries({ predicate: (q) => q.queryKey[0] === 'ticket' && q.queryKey[2] === true })
  }, [admin, queryClient])

  // Log out when the token expires, rather than letting the next save fail.
  useEffect(() => {
    if (!admin) return
    const ms = new Date(admin.expiresAt).getTime() - Date.now()
    const timer = setTimeout(logout, Math.min(Math.max(ms, 0), MAX_TIMER_MS))
    return () => clearTimeout(timer)
  }, [admin, logout])

  const value = useMemo(() => ({ admin, login, logout }), [admin, login, logout])
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthState {
  const auth = useContext(AuthContext)
  if (!auth) throw new Error('useAuth must be used inside <AuthProvider>')
  return auth
}
