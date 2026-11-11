import { Link } from 'react-router'

export function NotFoundPage({ what = 'page' }: { what?: string }) {
  return (
    <div className="mx-auto max-w-md py-16 text-center">
      <p className="font-mono text-sm text-brand-600">404</p>
      <h1 className="mt-2 text-2xl font-semibold text-slate-900">This {what} does not exist</h1>
      <p className="mt-2 text-sm text-slate-600">Check the link, or go back to the list.</p>
      <Link to="/tickets" className="mt-6 inline-block font-semibold text-brand-700 underline underline-offset-2">
        All tickets
      </Link>
    </div>
  )
}
