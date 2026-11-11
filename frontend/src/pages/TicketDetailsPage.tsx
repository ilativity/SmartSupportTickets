import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState, type FormEvent } from 'react'
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
  // An image the server has but the browser could not draw (e.g. a damaged file): shown as a placeholder. This is
  // display only — whether an image may be ADDED is the server's `imageAvailable`, never this.
  const [failedImage, setFailedImage] = useState<string | null>(null)

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
  const showPlaceholder = !t.imageAvailable || failedImage === t.imageUrl
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

        <TicketImage url={t.imageUrl} failed={showPlaceholder} onFailed={() => setFailedImage(t.imageUrl)} />

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
        // Keyed by the saved status and resolution: the form restarts from the server's values when those change, but
        // not when only an image was added — that must not throw away an edit the admin has not saved yet.
        <EditTicketForm key={`${t.status}|${t.resolution}`} ticket={t} canAddImage={!t.imageAvailable} />
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
 * placeholder. Adding an image (admin) lives in the edit form, as one more unsaved change.
 */
function TicketImage({ url, failed, onFailed }: { url: string | null; failed: boolean; onFailed: () => void }) {
  if (!url) return null
  return (
    <section className="mt-5">
      <h2 className="text-sm font-semibold text-slate-700">Image</h2>
      {failed ? (
        <div className="mt-1 flex h-32 items-center justify-center rounded-xl border border-dashed border-slate-300 text-sm text-slate-500">
          Image not available
        </div>
      ) : (
        <img src={url} alt="Attached by the customer" onError={onFailed} className="mt-1 max-h-96 rounded-xl border border-slate-200 object-contain" />
      )}
    </section>
  )
}

/**
 * The admin's edit: status, resolution and — when the ticket has no working image — an image to add. All three are
 * unsaved changes until Save; Discard drops all of them.
 */
function EditTicketForm({ ticket, canAddImage }: { ticket: TicketDetails; canAddImage: boolean }) {
  const [status, setStatus] = useState(ticket.status)
  const [resolution, setResolution] = useState(ticket.resolution)
  const [image, setImage] = useState<File | null>(null)
  const [preview, setPreview] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [imageError, setImageError] = useState<string | null>(null)
  const statuses = useQuery({ queryKey: ['statuses'], queryFn: getStatuses, staleTime: Infinity })
  const { run, busy } = useInflightGuard()
  const queryClient = useQueryClient()
  const toast = useToast()

  // A local preview URL for the chosen image, released when it changes or the form goes away (no memory leak).
  useEffect(() => {
    if (!image) {
      setPreview(null)
      return
    }
    const url = URL.createObjectURL(image)
    setPreview(url)
    return () => URL.revokeObjectURL(url)
  }, [image])

  const fieldsDirty = status !== ticket.status || resolution.trim() !== ticket.resolution
  // A chosen image counts only while the ticket can still take one: once another admin's image arrives, ours is no
  // longer a change (and must not block saving status/resolution).
  const pendingImage = canAddImage ? image : null
  const dirty = fieldsDirty || pendingImage !== null

  function chooseImage(file: File | undefined) {
    const problem = file ? checkImage(file) : undefined
    setImageError(problem ?? null)
    setImage(file && !problem ? file : null)
  }

  function discard() {
    setStatus(ticket.status)
    setResolution(ticket.resolution)
    setImage(null)
    setError(null)
    setImageError(null)
  }

  /** Keeps the admin view in the cache only while still logged in (a logout mid-save must not leave a full email). */
  function cache(saved: TicketDetails) {
    if (tokenStore.get()) queryClient.setQueryData(['ticket', ticket.id, true], saved)
  }

  async function save(event: FormEvent) {
    event.preventDefault()
    if (resolution.trim().length > 2000) {
      setError('Resolution must be at most 2000 characters.')
      return
    }
    await run(async () => {
      setError(null)
      // The image first: if it is refused (not an image, or someone added one meanwhile), nothing is saved and the
      // admin's status/resolution edit stays in the form to try again.
      let imageSaved = false
      if (pendingImage) {
        try {
          cache(await attachImage(ticket.id, pendingImage))
          setImage(null)
          imageSaved = true
        } catch (e) {
          if (e instanceof ApiError && e.status === 409) {
            // The server already has an image for this ticket (e.g. another admin was faster): drop ours, reload the
            // ticket to show theirs, keep the other edits for another Save.
            setImage(null)
            setError('This ticket already has an image, so yours was not added. Nothing else was saved yet — press Save again.')
            void queryClient.invalidateQueries({ queryKey: ['ticket', ticket.id] })
          } else {
            setImageError(e instanceof ApiError ? (e.fieldErrors.image?.[0] ?? e.message) : 'Could not add the image.')
          }
          return
        }
      }

      if (fieldsDirty) {
        try {
          cache(await updateTicket(ticket.id, { status, resolution }))
        } catch (e) {
          const reason = e instanceof ApiError ? (Object.values(e.fieldErrors)[0]?.[0] ?? e.message) : 'Could not save.'
          // Say exactly what happened: the image is stored even though the status/resolution were not.
          setError(imageSaved ? `The image was added, but status and resolution were not saved: ${reason}` : reason)
          void queryClient.invalidateQueries({ queryKey: ['tickets'] })
          return
        }
      }

      void queryClient.invalidateQueries({ queryKey: ['tickets'] })
      // Emails go out for status/resolution changes only; an added image alone notifies nobody.
      toast(fieldsDirty ? 'Saved. The customer was notified by email.' : 'Saved.')
    })
  }

  return (
    <form onSubmit={save} className="mt-4 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm sm:p-6">
      <h2 className="text-lg font-semibold text-slate-900">Manage ticket</h2>
      <div className="mt-4 grid gap-4 sm:grid-cols-[12rem_1fr]">
        <Field id="edit-status" label="Status">
          {/* Locked while saving: the form restarts from the server's values afterwards, so typing then would be lost. */}
          <select id="edit-status" className={inputClass} value={status} disabled={busy} onChange={(e) => setStatus(e.target.value)}>
            {(statuses.data ?? [ticket.status]).map((s) => (
              <option key={s} value={s}>{s}</option>
            ))}
          </select>
        </Field>
        <Field id="edit-resolution" label="Resolution">
          <textarea id="edit-resolution" rows={4} maxLength={2000} className={inputClass} value={resolution} disabled={busy} onChange={(e) => setResolution(e.target.value)} />
        </Field>
      </div>

      {canAddImage && (
        <div className="mt-4">
          <Field id="edit-image" label="Image" error={imageError ?? undefined} hint="The ticket has no image. JPEG, PNG or WebP, up to 5 MB.">
            {preview ? (
              <div className="flex flex-wrap items-center gap-3">
                <img src={preview} alt="Selected image preview" className="size-20 rounded-lg border border-slate-200 object-cover" />
                <Button type="button" variant="secondary" onClick={() => chooseImage(undefined)} disabled={busy}>
                  Remove image
                </Button>
              </div>
            ) : (
              <input
                id="edit-image"
                type="file"
                accept={IMAGE_TYPES.join(',')}
                onChange={(e) => {
                  chooseImage(e.target.files?.[0])
                  e.target.value = '' // choosing the same file again after removing it must fire onChange
                }}
                aria-invalid={!!imageError}
                aria-describedby={imageError ? 'edit-image-error' : undefined}
                className="block w-full text-sm text-slate-700 file:mr-3 file:min-h-11 file:cursor-pointer file:rounded-lg file:border-0 file:bg-brand-50 file:px-4 file:font-semibold file:text-brand-700 hover:file:bg-brand-100"
              />
            )}
          </Field>
        </div>
      )}

      {error && <p role="alert" className="mt-3 rounded-lg bg-red-50 p-3 text-sm text-red-800">{error}</p>}
      <div className="mt-4 flex flex-wrap items-center justify-end gap-3">
        {dirty && <span className="text-sm text-slate-500">Unsaved changes</span>}
        <Button type="button" variant="secondary" disabled={!dirty || busy} onClick={discard}>
          Discard changes
        </Button>
        <Button type="submit" busy={busy} disabled={!dirty}>
          Save
        </Button>
      </div>
    </form>
  )
}
