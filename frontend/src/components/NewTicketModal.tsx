import { useEffect, useRef, useState, type FormEvent } from 'react'
import { ApiError } from '../api/client'
import type { TicketDetails } from '../api/types'
import { createTicket } from '../api/tickets'
import { useInflightGuard } from '../hooks/useInflightGuard'
import { IMAGE_TYPES, checkImage } from '../lib/images'
import { Button, Field, inputClass } from './ui'

type Errors = Partial<Record<'name' | 'email' | 'description' | 'image' | 'form', string>>

/**
 * The same rules the API enforces (it re-checks everything). Checking here too only spares the customer a round trip;
 * the server's answer is the one that counts and its messages are shown the same way.
 */
function validate(name: string, email: string, description: string): Errors {
  const errors: Errors = {}
  const n = name.trim().length
  if (n === 0) errors.name = 'Name is required.'
  else if (n < 2 || n > 100) errors.name = 'Name must be between 2 and 100 characters.'
  if (!email.trim()) errors.email = 'Email is required.'
  else if (!/^[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+$/.test(email.trim()))
    errors.email = 'Enter a valid email address, e.g. name@example.com.'
  const d = description.trim().length
  if (d === 0) errors.description = 'Description is required.'
  else if (d < 10 || d > 2000) errors.description = 'Description must be between 10 and 2000 characters.'
  return errors
}

/**
 * "New ticket" form in a native <dialog>: the browser provides the focus trap, Esc to close and the backdrop.
 * Full screen on phones, a centred card from the sm breakpoint up.
 */
export function NewTicketModal({
  open,
  onClose,
  onCreated,
}: {
  open: boolean
  onClose: () => void
  onCreated: (ticket: TicketDetails) => void
}) {
  const dialog = useRef<HTMLDialogElement>(null)
  const opener = useRef<Element | null>(null)
  const [name, setName] = useState('')
  const [email, setEmail] = useState('')
  const [description, setDescription] = useState('')
  const [image, setImage] = useState<File | null>(null)
  const [preview, setPreview] = useState<string | null>(null)
  const [errors, setErrors] = useState<Errors>({})
  const { run, busy } = useInflightGuard()

  // A local preview URL for the chosen image, released when it changes or the form closes (no memory leak).
  useEffect(() => {
    if (!image) {
      setPreview(null)
      return
    }
    const url = URL.createObjectURL(image)
    setPreview(url)
    return () => URL.revokeObjectURL(url)
  }, [image])

  function chooseImage(file: File | undefined) {
    const problem = file ? checkImage(file) : undefined
    setErrors((current) => ({ ...current, image: problem }))
    setImage(file && !problem ? file : null)
  }

  useEffect(() => {
    const d = dialog.current
    if (!d) return
    if (open && !d.open) {
      opener.current = document.activeElement // focus goes back here on close
      d.showModal()
      // Explicitly: React's autoFocus prop is applied on mount, not as the HTML attribute <dialog> looks for.
      document.getElementById('ticket-name')?.focus()
    } else if (!open && d.open) {
      d.close()
    }
  }, [open])

  function handleClose() {
    onClose()
    setErrors({})
    setImage(null)
    if (opener.current instanceof HTMLElement) opener.current.focus()
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    const found = validate(name, email, description)
    setErrors(found)
    const firstInvalid = (['name', 'email', 'description'] as const).find((field) => found[field])
    if (firstInvalid) {
      // By id, not by [aria-invalid]: React has not re-rendered the error state yet at this point.
      document.getElementById(`ticket-${firstInvalid}`)?.focus()
      return
    }

    await run(async () => {
      try {
        const ticket = await createTicket({ name, email, description, image })
        setName('')
        setEmail('')
        setDescription('')
        setImage(null)
        onCreated(ticket)
      } catch (error) {
        if (error instanceof ApiError && Object.keys(error.fieldErrors).length) {
          const fe = error.fieldErrors
          const shown = ['name', 'email', 'description', 'image']
          // Messages for fields this form does not show still have to reach the user.
          const other = Object.entries(fe).filter(([key]) => !shown.includes(key)).flatMap(([, messages]) => messages)
          setErrors({
            name: fe.name?.[0],
            email: fe.email?.[0],
            description: fe.description?.[0],
            image: fe.image?.[0],
            form: other.length ? other.join(' ') : undefined,
          })
          const first = shown.find((key) => fe[key]?.length)
          if (first) document.getElementById(`ticket-${first}`)?.focus()
        } else {
          setErrors({ form: error instanceof ApiError ? error.message : 'Could not create the ticket.' })
        }
      }
    })
  }

  return (
    <dialog
      ref={dialog}
      onClose={handleClose}
      aria-labelledby="new-ticket-title"
      className="m-0 h-full max-h-none w-full max-w-none bg-white p-0 sm:m-auto sm:h-auto sm:max-w-lg sm:rounded-2xl sm:shadow-xl"
    >
      <form onSubmit={submit} noValidate className="flex h-full flex-col">
        <div className="flex items-center justify-between border-b border-slate-200 px-5 py-4">
          <h2 id="new-ticket-title" className="text-lg font-semibold text-slate-900">New ticket</h2>
          <button
            type="button"
            onClick={() => dialog.current?.close()}
            className="rounded-lg p-2 text-slate-500 hover:bg-slate-100"
            aria-label="Close"
          >
            <span aria-hidden="true" className="text-xl leading-none">×</span>
          </button>
        </div>

        <div className="flex-1 space-y-4 overflow-y-auto px-5 py-5">
          <p className="text-xs text-slate-500">
            Fields marked <span aria-hidden="true" className="text-red-700">*</span>
            <span className="sr-only">with an asterisk</span> are required.
          </p>
          <Field id="ticket-name" label="Name" error={errors.name} required>
            <input
              id="ticket-name"
              className={inputClass}
              value={name}
              onChange={(e) => setName(e.target.value)}
              aria-required="true"
              autoComplete="name"
              maxLength={100}
              aria-invalid={!!errors.name}
              aria-describedby={errors.name ? 'ticket-name-error' : undefined}
            />
          </Field>
          <Field id="ticket-email" label="Email address" error={errors.email} hint="We email you a link to follow your ticket." required>
            <input
              id="ticket-email"
              type="email"
              inputMode="email"
              className={inputClass}
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              aria-required="true"
              autoComplete="email"
              maxLength={254}
              aria-invalid={!!errors.email}
              aria-describedby={errors.email ? 'ticket-email-error' : undefined}
            />
          </Field>
          <Field id="ticket-description" label="Description" error={errors.description} required>
            <textarea
              id="ticket-description"
              rows={5}
              aria-required="true"
              className={inputClass}
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              maxLength={2000}
              aria-invalid={!!errors.description}
              aria-describedby={errors.description ? 'ticket-description-error' : undefined}
            />
          </Field>
          <Field id="ticket-image" label="Image (optional)" error={errors.image} hint="JPEG, PNG or WebP, up to 5 MB.">
            {preview ? (
              <div className="flex items-center gap-3">
                <img src={preview} alt="Selected image preview" className="size-20 rounded-lg border border-slate-200 object-cover" />
                <Button type="button" variant="secondary" onClick={() => chooseImage(undefined)}>
                  Remove image
                </Button>
              </div>
            ) : (
              <input
                id="ticket-image"
                type="file"
                accept={IMAGE_TYPES.join(',')}
                onChange={(e) => {
                  chooseImage(e.target.files?.[0])
                  e.target.value = '' // choosing the same file again after removing it must fire onChange
                }}
                aria-invalid={!!errors.image}
                aria-describedby={errors.image ? 'ticket-image-error' : undefined}
                className="block w-full text-sm text-slate-700 file:mr-3 file:min-h-11 file:cursor-pointer file:rounded-lg file:border-0 file:bg-brand-50 file:px-4 file:font-semibold file:text-brand-700 hover:file:bg-brand-100"
              />
            )}
          </Field>
          {errors.form && (
            <p role="alert" className="rounded-lg bg-red-50 p-3 text-sm text-red-800">{errors.form}</p>
          )}
        </div>

        <div className="border-t border-slate-200 px-5 py-4">
          <Button type="submit" variant="accent" busy={busy} className="w-full">
            {busy ? 'Creating ticket and summary…' : 'Submit'}
          </Button>
        </div>
      </form>
    </dialog>
  )
}
