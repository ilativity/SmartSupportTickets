import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react'
import { Link } from 'react-router'

interface ToastMessage {
  id: number
  text: string
  link?: { to: string; label: string }
  tone: 'success' | 'error'
}

type ShowToast = (text: string, options?: { link?: ToastMessage['link']; tone?: ToastMessage['tone'] }) => void

const ToastContext = createContext<ShowToast>(() => {})

/** One message at a time, announced to screen readers, gone after a few seconds. */
export function ToastProvider({ children }: { children: ReactNode }) {
  const [toast, setToast] = useState<ToastMessage | null>(null)

  const show = useCallback<ShowToast>((text, options) => {
    setToast({ id: Date.now(), text, link: options?.link, tone: options?.tone ?? 'success' })
  }, [])

  useEffect(() => {
    if (!toast) return
    const timer = setTimeout(() => setToast(null), 6000)
    return () => clearTimeout(timer)
  }, [toast])

  return (
    <ToastContext.Provider value={show}>
      {children}
      {/* Top of the screen, under the header: the bottom is taken by the Save bar while editing. */}
      <div aria-live="polite" className="pointer-events-none fixed inset-x-0 top-16 z-50 flex justify-center px-4">
        {toast && (
          <div
            key={toast.id}
            role="status"
            className={`pointer-events-auto flex max-w-md items-center gap-3 rounded-xl px-4 py-3 text-sm text-white shadow-lg ${toast.tone === 'error' ? 'bg-red-700' : 'bg-slate-900'}`}
          >
            <span>{toast.text}</span>
            {toast.link && (
              <Link to={toast.link.to} className="font-semibold underline underline-offset-2" onClick={() => setToast(null)}>
                {toast.link.label}
              </Link>
            )}
          </div>
        )}
      </div>
    </ToastContext.Provider>
  )
}

export const useToast = () => useContext(ToastContext)
