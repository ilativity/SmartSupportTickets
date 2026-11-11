import { expect, test } from '@playwright/test'
import { uniqueName } from './helpers'

// Spec 2: anonymous customer creates a ticket through the modal.
test('anonymous create: validation errors, then success lands on the new ticket', async ({ page }) => {
  await page.goto('/tickets')
  await page.getByRole('button', { name: 'New ticket', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: 'New ticket' })
  await expect(dialog).toBeVisible()
  await expect(dialog.getByLabel('Name')).toBeFocused()

  // Required fields are marked before anything is submitted, visibly and for screen readers.
  await expect(dialog.getByText('are required.')).toBeVisible()
  // By role and exact accessible name: the asterisk is visual only, a screen reader hears "Name, required".
  for (const label of ['Name', 'Email address', 'Description']) {
    await expect(dialog.getByRole('textbox', { name: label, exact: true })).toHaveAttribute('aria-required', 'true')
  }
  await expect(dialog.getByLabel('Image (optional)')).not.toHaveAttribute('aria-required', 'true')

  await dialog.getByRole('button', { name: 'Submit', exact: true }).click()
  await expect(dialog.getByText('Name is required.')).toBeVisible()
  await expect(dialog.getByText('Email is required.')).toBeVisible()
  await expect(dialog.getByText('Description is required.')).toBeVisible()

  const name = uniqueName('Create')
  await dialog.getByLabel('Name').fill(name)
  await dialog.getByLabel('Email address').fill('not-an-email')
  await dialog.getByLabel('Description').fill('The office printer jams on every second page.')
  await dialog.getByRole('button', { name: 'Submit', exact: true }).click()
  await expect(dialog.getByText(/valid email address/)).toBeVisible()
  await expect(dialog.getByLabel('Email address')).toBeFocused()

  await dialog.getByLabel('Email address').fill('customer@example.com')
  await dialog.getByRole('button', { name: 'Submit', exact: true }).click()

  await expect(page).toHaveURL(/\/tickets\/[0-9a-f-]{36}$/)
  await expect(page.getByRole('heading', { level: 1, name })).toBeVisible()
  await expect(page.getByText('c***@example.com')).toBeVisible()
  await expect(page.getByRole('status').filter({ hasText: 'Ticket created' })).toBeVisible()

  // And it is in the list.
  await page.getByRole('link', { name: '← All tickets' }).click()
  await page.getByLabel('Search', { exact: true }).fill(name)
  await expect(page.getByRole('link', { name, exact: true })).toBeVisible()
})

test('buttons show the pointer cursor, including the dialog close button', async ({ page }) => {
  await page.goto('/tickets')
  await page.getByRole('button', { name: 'New ticket', exact: true }).click()
  const close = page.getByRole('dialog', { name: 'New ticket' }).getByRole('button', { name: 'Close', exact: true })
  expect(await close.evaluate((el) => getComputedStyle(el).cursor)).toBe('pointer')
})

test('Escape closes the modal and focus returns to the button', async ({ page }) => {
  await page.goto('/tickets')
  const opener = page.getByRole('button', { name: 'New ticket', exact: true })
  await opener.click()
  await expect(page.getByRole('dialog', { name: 'New ticket' })).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(page.getByRole('dialog', { name: 'New ticket' })).toBeHidden()
  await expect(opener).toBeFocused()
})
