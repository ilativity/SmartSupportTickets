import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter, Link, Navigate, Route, Routes } from 'react-router'
import { ApiError } from './api/client'
import { AuthProvider, useAuth } from './auth/AuthContext'
import { ToastProvider } from './components/Toast'
import { LoginPage } from './pages/LoginPage'
import { NotFoundPage } from './pages/NotFoundPage'
import { TicketDetailsPage } from './pages/TicketDetailsPage'
import { TicketsPage } from './pages/TicketsPage'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // A 4xx answer will not change on retry; one retry covers a brief network blip or a 5xx.
      retry: (failures, error) => failures < 1 && !(error instanceof ApiError && error.status >= 400 && error.status < 500),
      refetchOnWindowFocus: false,
    },
  },
})

function Header() {
  const { admin, logout } = useAuth()
  return (
    <header className="bg-brand-600 text-white">
      <div className="mx-auto flex h-14 max-w-6xl items-center justify-between gap-3 px-4">
        <Link to="/tickets" className="truncate text-lg font-semibold tracking-tight">
          Smart Support <span className="font-normal text-brand-100">Tickets</span>
        </Link>
        {admin ? (
          <div className="flex items-center gap-3 text-sm">
            <span className="hidden text-brand-100 sm:inline">Signed in as {admin.username}</span>
            <button type="button" onClick={logout} className="rounded-lg px-3 py-2 font-semibold ring-1 ring-white/40 hover:bg-white/10">
              Log out
            </button>
          </div>
        ) : (
          <Link to="/login" className="rounded-lg px-3 py-2 text-sm font-semibold ring-1 ring-white/40 hover:bg-white/10">
            Admin login
          </Link>
        )}
      </div>
    </header>
  )
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <AuthProvider>
          <ToastProvider>
            <Header />
            <main className="mx-auto max-w-6xl px-4 py-6">
              <Routes>
                <Route path="/" element={<Navigate to="/tickets" replace />} />
                <Route path="/tickets" element={<TicketsPage />} />
                <Route path="/tickets/:id" element={<TicketDetailsPage />} />
                <Route path="/login" element={<LoginPage />} />
                <Route path="*" element={<NotFoundPage />} />
              </Routes>
            </main>
          </ToastProvider>
        </AuthProvider>
      </BrowserRouter>
    </QueryClientProvider>
  )
}
