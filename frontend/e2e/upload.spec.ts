import { expect, test } from '@playwright/test'
import { uniqueName } from './helpers'

// A real 1x1 PNG, so the browser can actually decode what the server stores and serves back.
const PNG_1X1 = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
  'base64',
)

async function openForm(page: import('@playwright/test').Page, name: string) {
  await page.goto('/tickets')
  await page.getByRole('button', { name: 'New ticket', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: 'New ticket' })
  await dialog.getByLabel('Name').fill(name)
  await dialog.getByLabel('Email address').fill('uploader@example.com')
  await dialog.getByLabel('Description').fill('The screen shows stripes after the update, photo attached.')
  return dialog
}

test('a ticket with an image: preview in the form, image shown on the ticket page', async ({ page }) => {
  const dialog = await openForm(page, uniqueName('Upload'))
  await dialog.getByLabel('Image (optional)').setInputFiles({ name: 'screen.png', mimeType: 'image/png', buffer: PNG_1X1 })
  await expect(dialog.getByAltText('Selected image preview')).toBeVisible()

  await dialog.getByRole('button', { name: 'Submit', exact: true }).click()

  await expect(page).toHaveURL(/\/tickets\/[0-9a-f-]{36}$/)
  const image = page.getByAltText('Attached by the customer')
  await expect(image).toBeVisible()
  await expect(image).toHaveAttribute('src', /^\/uploads\/[0-9a-f]{32}\.png$/)
  // Decoded by the browser, i.e. the served bytes really are the uploaded image.
  expect(await image.evaluate((img: HTMLImageElement) => img.naturalWidth)).toBe(1)
})

test('a text file pretending to be a PNG is refused by the server, with the message at the field', async ({ page }) => {
  const dialog = await openForm(page, uniqueName('Fake image'))
  // The browser reports image/png (the client-side check passes); only the bytes reveal it is text.
  await dialog.getByLabel('Image (optional)').setInputFiles({ name: 'evil.png', mimeType: 'image/png', buffer: Buffer.from('<script>alert(1)</script>') })
  await dialog.getByRole('button', { name: 'Submit', exact: true }).click()

  await expect(dialog.getByText('The image must be a JPEG, PNG or WebP file.')).toBeVisible()
  await expect(page).toHaveURL(/\/tickets$/)
})

test('the client refuses an image over 5 MB before uploading it', async ({ page }) => {
  const dialog = await openForm(page, uniqueName('Big image'))
  const big = Buffer.alloc(5 * 1024 * 1024 + 1)
  PNG_1X1.copy(big)
  await dialog.getByLabel('Image (optional)').setInputFiles({ name: 'big.png', mimeType: 'image/png', buffer: big })

  await expect(dialog.getByText('The image must be at most 5 MB.')).toBeVisible()
  await expect(dialog.getByAltText('Selected image preview')).toHaveCount(0)
})

async function adminOnNewTicket(page: import('@playwright/test').Page, request: import('@playwright/test').APIRequestContext) {
  const created = await request.post('/api/tickets', {
    multipart: { name: uniqueName('Add image'), email: 'later@example.com', description: 'The photo will be added by support later.' },
  })
  const ticket = (await created.json()) as { id: string }
  await page.goto('/login')
  await page.getByLabel('Username').fill('admin')
  await page.getByLabel('Password').fill('Admin123!')
  await page.getByRole('button', { name: 'Log in', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Log out', exact: true })).toBeVisible()
  await page.goto(`/tickets/${ticket.id}`)
  return ticket
}

test('an admin adds an image as part of the edit: one Save stores image, status and resolution', async ({ page, request }) => {
  await adminOnNewTicket(page, request)
  const save = page.getByRole('button', { name: 'Save', exact: true })
  await expect(save).toBeDisabled()

  // Choosing an image alone is an unsaved change.
  await page.getByLabel('Image', { exact: true }).setInputFiles({ name: 'later.png', mimeType: 'image/png', buffer: PNG_1X1 })
  await expect(page.getByAltText('Selected image preview')).toBeVisible()
  await expect(page.getByText('Unsaved changes')).toBeVisible()
  await expect(save).toBeEnabled()

  await page.getByLabel('Status').selectOption('Resolved')
  await page.getByLabel('Resolution').fill('Photo received, part replaced.')
  await save.click()

  await expect(page.getByRole('status').filter({ hasText: 'Saved. The customer was notified' })).toBeVisible()
  const image = page.getByAltText('Attached by the customer')
  await expect(image).toBeVisible()
  expect(await image.evaluate((img: HTMLImageElement) => img.naturalWidth)).toBe(1)
  await expect(page.getByLabel('Image', { exact: true })).toHaveCount(0) // a working image is not offered for replacement
  await expect(page.getByText('Unsaved changes')).toHaveCount(0)

  await page.reload()
  await expect(page.getByAltText('Attached by the customer')).toBeVisible()
  await expect(page.getByLabel('Status')).toHaveValue('Resolved')
})

test('Discard changes drops a chosen image together with the other edits', async ({ page, request }) => {
  await adminOnNewTicket(page, request)

  await page.getByLabel('Resolution').fill('Not meant to be saved')
  await page.getByLabel('Image', { exact: true }).setInputFiles({ name: 'later.png', mimeType: 'image/png', buffer: PNG_1X1 })
  await page.getByRole('button', { name: 'Discard changes', exact: true }).click()

  await expect(page.getByAltText('Selected image preview')).toHaveCount(0)
  await expect(page.getByLabel('Resolution')).toHaveValue('')
  await expect(page.getByRole('button', { name: 'Save', exact: true })).toBeDisabled()
  await page.reload()
  await expect(page.getByAltText('Attached by the customer')).toHaveCount(0) // nothing was uploaded
})

test('saving only an image sends no email', async ({ page, request }) => {
  await adminOnNewTicket(page, request)

  await page.getByLabel('Image', { exact: true }).setInputFiles({ name: 'later.png', mimeType: 'image/png', buffer: PNG_1X1 })
  await page.getByRole('button', { name: 'Save', exact: true }).click()

  await expect(page.getByRole('status').filter({ hasText: /^Saved\.$/ })).toBeVisible()
  await expect(page.getByAltText('Attached by the customer')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Save', exact: true })).toBeDisabled()
})

test('visitors are not offered to add an image', async ({ page }) => {
  await page.goto('/tickets/b2a1f7c0-2a93-4d8f-a23e-9b1a24dcf5a1') // sample ticket with a missing image file
  await expect(page.getByText('Image not available')).toBeVisible()
  await expect(page.getByLabel('Image', { exact: true })).toHaveCount(0)
})

test('if another admin added an image first, the other edits can still be saved', async ({ page, request }) => {
  const ticket = await adminOnNewTicket(page, request)
  // Stub: the server answers the image upload with 409, as when someone else was faster.
  await page.route(`**/api/tickets/${ticket.id}/image`, (route) =>
    route.fulfill({ status: 409, contentType: 'application/problem+json', body: JSON.stringify({ status: 409, detail: 'This ticket already has an image.' }) }),
  )
  await page.getByLabel('Status').selectOption('Closed')
  await page.getByLabel('Image', { exact: true }).setInputFiles({ name: 'late.png', mimeType: 'image/png', buffer: PNG_1X1 })
  await page.getByRole('button', { name: 'Save', exact: true }).click()

  await expect(page.getByRole('alert')).toContainText('This ticket already has an image, so yours was not added')
  await expect(page.getByLabel('Status')).toHaveValue('Closed') // the edit is kept
  await page.getByRole('button', { name: 'Save', exact: true }).click() // and can now be saved
  await expect(page.getByRole('status').filter({ hasText: 'Saved. The customer was notified' })).toBeVisible()
})

test('if the status cannot be saved after the image was, the message says so', async ({ page, request }) => {
  const ticket = await adminOnNewTicket(page, request)
  await page.route(`**/api/tickets/${ticket.id}`, (route) =>
    route.request().method() === 'PUT'
      ? route.fulfill({ status: 500, contentType: 'application/problem+json', body: JSON.stringify({ status: 500 }) })
      : route.fallback(),
  )
  await page.getByLabel('Status').selectOption('Closed')
  await page.getByLabel('Image', { exact: true }).setInputFiles({ name: 'ok.png', mimeType: 'image/png', buffer: PNG_1X1 })
  await page.getByRole('button', { name: 'Save', exact: true }).click()

  await expect(page.getByRole('alert')).toContainText('The image was added, but status and resolution were not saved')
  await expect(page.getByAltText('Attached by the customer')).toBeVisible()
  await expect(page.getByLabel('Status')).toHaveValue('Closed')
})

test('an image the server has but the browser cannot draw is not offered for replacement', async ({ page, request }) => {
  // Regression (2026-11-12): only a PNG signature — accepted by the server's byte check, undrawable by a browser.
  // The page used to treat the failed drawing as "no image", offered an upload, and the server answered 409.
  const created = await request.post('/api/tickets', {
    multipart: {
      name: uniqueName('Damaged image'),
      email: 'damaged@example.com',
      description: 'The attached photo file is damaged.',
      image: { name: 'damaged.png', mimeType: 'image/png', buffer: Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0, 0, 0, 0x0d, 0x49, 0x48, 0x44, 0x52]) },
    },
  })
  const ticket = (await created.json()) as { id: string }
  await page.goto('/login')
  await page.getByLabel('Username').fill('admin')
  await page.getByLabel('Password').fill('Admin123!')
  await page.getByRole('button', { name: 'Log in', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Log out', exact: true })).toBeVisible()
  await page.goto(`/tickets/${ticket.id}`)

  await expect(page.getByText('Image not available')).toBeVisible()
  await expect(page.getByLabel('Image', { exact: true })).toHaveCount(0)
})
