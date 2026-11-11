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

test('an admin adds an image to a ticket whose image is missing; a working image cannot be replaced', async ({ page, request }) => {
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

  // An unsaved edit typed before the upload must survive it.
  await page.getByLabel('Resolution').fill('Typed before the upload')
  await page.getByLabel('Add an image').setInputFiles({ name: 'later.png', mimeType: 'image/png', buffer: PNG_1X1 })
  await page.getByRole('button', { name: 'Upload image', exact: true }).click()

  const image = page.getByAltText('Attached by the customer')
  await expect(image).toBeVisible()
  expect(await image.evaluate((img: HTMLImageElement) => img.naturalWidth)).toBe(1)
  await expect(page.getByLabel('Add an image')).toHaveCount(0) // a working image is not offered for replacement
  await expect(page.getByLabel('Resolution')).toHaveValue('Typed before the upload')

  await page.reload()
  await expect(page.getByAltText('Attached by the customer')).toBeVisible()
})

test('visitors are not offered to add an image', async ({ page }) => {
  await page.goto('/tickets/b2a1f7c0-2a93-4d8f-a23e-9b1a24dcf5a1') // sample ticket with a missing image file
  await expect(page.getByText('Image not available')).toBeVisible()
  await expect(page.getByLabel('Add an image')).toHaveCount(0)
})
