// Small shared building blocks, so every page uses the same button, field and status styles.
import type { ButtonHTMLAttributes, ReactNode } from 'react'

type Variant = 'primary' | 'accent' | 'secondary'

const variants: Record<Variant, string> = {
  primary: 'bg-brand-600 text-white hover:bg-brand-700 disabled:bg-brand-600/60',
  accent: 'bg-accent-600 text-white hover:bg-accent-700 disabled:bg-accent-600/60',
  secondary: 'bg-white text-slate-700 ring-1 ring-slate-300 hover:bg-slate-50 disabled:text-slate-400',
}

export function Button({
  variant = 'primary',
  busy = false,
  className = '',
  children,
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: Variant; busy?: boolean }) {
  return (
    <button
      {...props}
      disabled={props.disabled || busy}
      aria-busy={busy || undefined}
      className={`inline-flex min-h-11 items-center justify-center gap-2 rounded-lg px-4 text-sm font-semibold transition-colors disabled:cursor-not-allowed ${variants[variant]} ${className}`}
    >
      {busy && <Spinner />}
      {children}
    </button>
  )
}

export function Spinner({ className = 'size-4' }: { className?: string }) {
  return (
    <span
      aria-hidden="true"
      className={`inline-block animate-spin rounded-full border-2 border-current border-r-transparent ${className}`}
    />
  )
}

export const inputClass =
  'block w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-base text-slate-900 shadow-sm placeholder:text-slate-400 focus:border-brand-600 focus:outline-none focus:ring-2 focus:ring-brand-600/30 aria-[invalid=true]:border-red-500 sm:text-sm'

/** A labelled form field with its error message linked for screen readers. */
export function Field({
  id,
  label,
  error,
  hint,
  required = false,
  children,
}: {
  id: string
  label: string
  error?: string
  hint?: string
  /** Shows a red asterisk. The control itself carries aria-required, so screen readers say "required". */
  required?: boolean
  children: ReactNode
}) {
  return (
    <div>
      <label htmlFor={id} className="mb-1 block text-sm font-medium text-slate-700">
        {label}
        {/* Hidden from screen readers: the accessible name stays "Name", and aria-required says the rest. */}
        {required && <span aria-hidden="true" className="ml-0.5 text-red-700">*</span>}
      </label>
      {children}
      {hint && !error && <p className="mt-1 text-xs text-slate-500">{hint}</p>}
      {error && (
        <p id={`${id}-error`} className="mt-1 text-sm text-red-700">
          {error}
        </p>
      )}
    </div>
  )
}

const statusStyles: Record<string, string> = {
  New: 'bg-sky-50 text-sky-800 ring-sky-600/20',
  'In Progress': 'bg-amber-50 text-amber-800 ring-amber-600/20',
  Resolved: 'bg-emerald-50 text-emerald-800 ring-emerald-600/20',
  Closed: 'bg-slate-100 text-slate-700 ring-slate-500/20',
}

export function StatusBadge({ status }: { status: string }) {
  return (
    <span
      className={`inline-flex items-center whitespace-nowrap rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset ${statusStyles[status] ?? statusStyles.Closed}`}
    >
      {status}
    </span>
  )
}

/** The ticket's short reference, e.g. "#b2a1f7": what a customer quotes on the phone. */
export function TicketRef({ id }: { id: string }) {
  return <span className="font-mono text-xs text-slate-500">#{id.slice(0, 6)}</span>
}

export function ErrorState({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <div role="alert" className="rounded-xl border border-red-200 bg-red-50 p-6 text-center">
      <p className="text-sm text-red-800">{message}</p>
      {onRetry && (
        <Button variant="secondary" className="mt-4" onClick={onRetry}>
          Retry
        </Button>
      )}
    </div>
  )
}

export function EmptyState({ children }: { children: ReactNode }) {
  return <div className="rounded-xl border border-dashed border-slate-300 p-10 text-center text-sm text-slate-600">{children}</div>
}
