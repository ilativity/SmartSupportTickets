import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Link, useLocation, useParams } from 'react-router'
import { ApiError, tokenStore } from '../api/client'
import { attachImage, getStatuses, getTicket, updateTicket } from '../api/tickets'
import type { TicketDetails } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { useToast } from '../components/Toast'
import { Button, ErrorState, Field, Spinner, StatusBadge, inputClass } from '../components/ui'
import { useInflightGuard } from '../hooks/useInflightGuard'
import { formatDate } from '../lib/format'
import { IMAGE_TYPES, checkImage } from '../lib/images'
import { NotFoundPage } from './NotFoundPage'

export function TicketDetailsPage() {
  const { id = '' } = useParams()
  const { admin } = useAuth()
  const location = useLocation()
  const ticket = useQuery({
    // The admin flag is part of the key: the same ticket looks different (full vs masked email) to an admin.
    queryKey: ['ticket', id, !!admin],
    queryFn: ({ signal }) => getTicket(id, signal),
  })

  if (ticket.isPending) {
    return (
      <div role="status" className="flex justify-center py-16 text-brand-600">
        <Spinner className="size-8" />
        <span className="sr-only">Loading ticket…</span>
      </div>
    )
  }
  if (ticket.isError) {
    if (ticket.error instanceof ApiError && ticket.error.isNotFound) return <NotFoundPage what="ticket" />
    return <ErrorState message={`Could not load the ticket. ${ticket.error.message}`} onRetry={() => ticket.refetch()} />
  }

  const t = ticket.data
  return (
    <div className="mx-auto max-w-3xl">
      <Link to="/tickets" className="text-sm font-medium text-brand-700 hover:underline">← All tickets</Link>

      <div className="mt-3 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm sm:p-6">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <h1 className="break-words text-2xl font-semibold text-slate-900">{t.name}</h1>
            <p className="mt-1 break-all text-sm text-slate-600">{t.email}</p>
          </div>
          <StatusBadge status={t.status} />
        </div>

        <CopyId id={t.id} />

        {t.summary && (
          <div className="mt-5 rounded-xl bg-brand-50 p-4">
            <p className="text-xs font-semibold uppercase tracking-wide text-brand-700">AI summary</p>
            <p className="mt-1 text-slate-800">{t.summary}</p>
          </div>
        )}

        <section className="mt-5">
          <h2 className="text-sm font-semibold text-slate-700">Description</h2>
          <p className="mt-1 whitespace-pre-wrap break-words text-slate-800">{t.description}</p>
        </section>

        {/* Keyed by the URL: a new image starts with a fresh "failed to load" state. */}
        <TicketImage key={t.imageUrl ?? 'none'} ticket={t} canAdd={!!admin} />

        {!admin && (
          <section className="mt-5">
            <h2 className="text-sm font-semibold text-slate-700">Resolution</h2>
            <p className="mt-1 whitespace-pre-wrap break-words text-slate-800">{t.resolution || <span className="text-slate-500">Not resolved yet.</span>}</p>
          </section>
        )}

        <dl className="mt-5 grid grid-cols-2 gap-3 border-t border-slate-100 pt-4 text-sm">
          <div>
            <dt className="text-slate-500">Created</dt>
            <dd className="text-slate-800">{formatDate(t.createdAt)}</dd>
          </div>
          <div>
            <dt className="text-slate-500">Last updated</dt>
            <dd className="text-slate-800">{formatDate(t.updatedAt)}</dd>
          </div>
        </dl>
      </div>

      {admin ? (
        // Keyed by updatedAt: when the saved ticket comes back, the form restarts from the server's values.
        // Keyed by the saved status and resolution: the form restarts when those change on the server, but not when
        // only the image was added — that must not throw away an edit the admin has not saved yet.
        <EditTicketForm key={`${t.status}|${t.resolution}`} ticket={t} />
      ) : (
        <p className="mt-4 text-sm text-slate-600">
          Support staff?{' '}
          <Link to="/login" state={{ from: location.pathname }} className="font-semibold text-brand-700 underline underline-offset-2">
            Log in to edit
          </Link>
        </p>
      )}
    </div>
  )
}

function CopyId({ id }: { id: string }) {
  const [copied, setCopied] = useState(false)
  return (
    <div className="mt-3 flex items-center gap-2 text-xs text-slate-500">
      <span className="font-mono break-all">{id}</span>
      <button
        type="button"
        className="rounded px-2 py-1 font-medium text-brand-700 hover:bg-brand-50"
        onClick={async () => {
          try {
            await navigator.clipboard.writeText(id)
            setCopied(true)
            setTimeout(() => setCopied(false), 2000)
          } catch {
            // clipboard blocked (insecure context): the id is visible and selectable anyway
          }
        }}
      >
        {copied ? 'Copied' : 'Copy ID'}
      </button>
    </div>
  )
}

/**
 * The ticket's image. The dataset's sample images do not exist on disk, so a broken image becomes a neutral
 * placeholder. An admin may add an image when there is none or only a broken link (never replace a working one).
 */
function TicketImage({ ticket, canAdd }: { ticket: TicketDetails; canAdd: boolean }) {
  const [failed, setFailed] = useState(false)
  const url = ticket.imageUrl
  const missing = !url || failed
  if (missing && !canAdd) {
    return url ? (
      <section className="mt-5">
        <h2 className="text-sm font-semibold text-slate-700">Image</h2>
        <ImagePlaceholder />
      </section>
    ) : null
  }

  return (
    <section className="mt-5">
      <h2 className="text-sm font-semibold text-slate-700">Image</h2>
      {missing ? (
        <>
          {url && <ImagePlaceholder />}
          {/* Keyed by the ticket, so a fresh form appears after the image was added. */}
          <AddImage key={ticket.updatedAt} ticketId={ticket.id} />
        </>
      ) : (
        <img src={url} alt="Attached by the customer" onError={() => setFailed(true)} className="mt-1 max-h-96 rounded-xl border border-slate-200 object-contain" />
      )}
    </section>
  )
}

function ImagePlaceholder() {
  return (
    <div className="mt-1 flex h-32 items-center justify-center rounded-xl border border-dashed border-slate-300 text-sm text-slate-500">
      Image not available
    </div>
  )
}

function AddImage({ ticketId }: { ticketId: string }) {
  const [file, setFile] = useState<File | null>(null)
  const [preview, setPreview] = useState<string | null>(null)
  const previewRef = useRef<string | null>(null)
  previewRef.current = preview
  // Release the preview's memory if the admin leaves the page with a file still selected.
  useEffect(() => () => {
    if (previewRef.current) URL.revokeObjectURL(previewRef.current)
  }, [])
  const [error, setError] = useState<string | null>(null)
  const { run, busy } = useInflightGuard()
  const queryClient = useQueryClient()
  const toast = useToast()

  function choose(chosen: File | undefined) {
    const problem = chosen ? checkImage(chosen) : undefined
    setError(problem ?? null)
    if (preview) URL.revokeObjectURL(preview)
    const ok = chosen && !problem ? chosen : null
    setFile(ok)
    setPreview(ok ? URL.createObjectURL(ok) : null)
  }

  async function upload() {
    if (!file) return
    await run(async () => {
      try {
        const saved = await attachImage(ticketId, file)
        if (preview) URL.revokeObjectURL(preview)
        // Only while still logged in: a logout during the upload must not leave the admin view (full email) cached.
        if (tokenStore.get()) queryClient.setQueryData(['ticket', ticketId, true], saved)
        void queryClient.invalidateQueries({ queryKey: ['tickets'] })
        toast('Image added.')
      } catch (e) {
        setError(e instanceof ApiError ? (e.fieldErrors.image?.[0] ?? e.message) : 'Could not add the image.')
        // 409: someone else added an image meanwhile — show theirs instead of a stale placeholder.
        if (e instanceof ApiError && e.status === 409) void queryClient.invalidateQueries({ queryKey: ['ticket', ticketId] })
      }
    })
  }

  return (
    <div className="mt-2 rounded-xl border border-dashed border-slate-300 p-4">
      <Field id="add-image" label="Add an image" error={error ?? undefined} hint="JPEG, PNG or WebP, up to 5 MB.">
        {preview ? (
          <div className="flex flex-wrap items-center gap-3">
            <img src={preview} alt="Selected image preview" className="size-20 rounded-lg border border-slate-200 object-cover" />
            <Button type="button" variant="secondary" onClick={() => choose(undefined)} disabled={busy}>
              Remove
            </Button>
            <Button type="button" onClick={upload} busy={busy}>
              Upload image
            </Button>
          </div>
        ) : (
          <input
            id="add-image"
            type="file"
            accept={IMAGE_TYPES.join(',')}
            onChange={(e) => {
              choose(e.target.files?.[0])
              e.target.value = ''
            }}
            aria-invalid={!!error}
            aria-describedby={error ? 'add-image-error' : undefined}
            className="block w-full text-sm text-slate-700 file:mr-3 file:min-h-11 file:cursor-pointer file:rounded-lg file:border-0 file:bg-brand-50 file:px-4 file:font-semibold file:text-brand-700 hover:file:bg-brand-100"
          />
        )}
      </Field>
    </div>
  )
}

function EditTicketForm({ ticket }: { ticket: TicketDetails }) {
  const [status, setStatus] = useState(ticket.status)
  const [resolution, setResolution] = useState(ticket.resolution)
  const [error, setError] = useState<string | null>(null)
  const statuses = useQuery({ queryKey: ['statuses'], queryFn: getStatuses, staleTime: Infinity })
  const { run, busy } = useInflightGuard()
  const queryClient = useQueryClient()
  const toast = useToast()

  const dirty = status !== ticket.status || resolution.trim() !== ticket.resolution

  async function save(event: FormEvent) {
    event.preventDefault()
    if (resolution.trim().length > 2000) {
      setError('Resolution must be at most 2000 characters.')
      return
    }
    await run(async () => {
      try {
        const saved = await updateTicket(ticket.id, { status, resolution })
        setError(null)
        // Only cache the admin view if still logged in: a logout during the save must not leave a full email behind.
        if (tokenStore.get()) queryClient.setQueryData(['ticket', ticket.id, true], saved)
        void queryClient.invalidateQueries({ queryKey: ['tickets'] })
        toast('Saved. The customer was notified by email.')
      } catch (e) {
        setError(e instanceof ApiError ? (Object.values(e.fieldErrors)[0]?.[0] ?? e.message) : 'Could not save.')
      }
    })
  }

  return (
    <form onSubmit={save} className="mt-4 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm sm:p-6">
      <h2 className="text-lg font-semibold text-slate-900">Manage ticket</h2>
      <div className="mt-4 grid gap-4 sm:grid-cols-[12rem_1fr]">
        <Field id="edit-status" label="Status">
          <select id="edit-status" className={inputClass} value={status} onChange={(e) => setStatus(e.target.value)}>
            {(statuses.data ?? [ticket.status]).map((s) => (
              <option key={s} value={s}>{s}</option>
            ))}
          </select>
        </Field>
        <Field id="edit-resolution" label="Resolution">
          <textarea id="edit-resolution" rows={4} maxLength={2000} className={inputClass} value={resolution} onChange={(e) => setResolution(e.target.value)} />
        </Field>
      </div>
      {error && <p role="alert" className="mt-3 rounded-lg bg-red-50 p-3 text-sm text-red-800">{error}</p>}
      <div className="mt-4 flex items-center justify-end gap-3">
        {dirty && <span className="text-sm text-slate-500">Unsaved changes</span>}
        <Button
          type="button"
          variant="secondary"
          disabled={!dirty || busy}
          onClick={() => {
            setStatus(ticket.status)
            setResolution(ticket.resolution)
            setError(null)
          }}
        >
          Discard changes
        </Button>
        <Button type="submit" busy={busy} disabled={!dirty}>
          Save
        </Button>
      </div>
    </form>
  )
}
