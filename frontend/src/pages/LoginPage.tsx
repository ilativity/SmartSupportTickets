import { useState, type FormEvent } from 'react'
import { useLocation, useNavigate } from 'react-router'
import { ApiError } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { Button, Field, inputClass } from '../components/ui'
import { useInflightGuard } from '../hooks/useInflightGuard'

export function LoginPage() {
  const { login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  // Back to where "Log in to edit" was clicked. Only a path on this site: "//host" and "/\host" are rejected because
  // browsers treat them as links to another site.
  const requested: unknown = location.state?.from
  const from =
    typeof requested === 'string' && requested.startsWith('/') && !requested.startsWith('//') && !requested.startsWith('/\\')
      ? requested
      : '/tickets'

  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const { run, busy } = useInflightGuard()

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!username.trim() || !password) {
      setError('Enter your username and password.')
      return
    }

    await run(async () => {
      try {
        await login(username.trim(), password)
        navigate(from, { replace: true })
      } catch (e) {
        setPassword('')
        document.getElementById('password')?.focus()
        // ApiError already turns 429 into "too many attempts" — never presented as a wrong password.
        setError(e instanceof ApiError ? e.message : 'Login failed. Please try again.')
      }
    })
  }

  return (
    <div className="mx-auto max-w-sm py-8">
      <div className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm">
        <h1 className="text-center text-2xl font-semibold text-slate-900">Admin login</h1>
        <p className="mt-1 text-center text-sm text-slate-600">Only support staff can change tickets.</p>
        <form onSubmit={submit} noValidate className="mt-6 space-y-4">
          <Field id="username" label="Username">
            <input id="username" className={inputClass} value={username} onChange={(e) => setUsername(e.target.value)} autoComplete="username" autoFocus />
          </Field>
          <Field id="password" label="Password">
            <input id="password" type="password" className={inputClass} value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="current-password" />
          </Field>
          {error && (
            <p role="alert" className="rounded-lg bg-red-50 p-3 text-sm text-red-800">{error}</p>
          )}
          <Button type="submit" variant="accent" busy={busy} className="w-full">
            Log in
          </Button>
        </form>
        {import.meta.env.DEV && (
          <p className="mt-4 text-center text-xs text-slate-500">
            Development: <span className="font-mono">admin</span> / <span className="font-mono">Admin123!</span>
          </p>
        )}
      </div>
    </div>
  )
}
