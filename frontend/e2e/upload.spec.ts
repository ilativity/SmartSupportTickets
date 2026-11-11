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
