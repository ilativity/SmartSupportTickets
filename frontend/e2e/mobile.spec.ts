import { expect, test, type Page } from '@playwright/test'
import { createTicket, logIn, uniqueName } from './helpers'

// Spec 7: a phone (375 px wide). Runs only in the "mobile" project of playwright.config.ts.

async function expectNoSidewaysScroll(page: Page) {
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)
  expect(overflow).toBe(0)
}

test('phone: cards instead of a table, no sideways scrolling', async ({ page }) => {
  await page.goto('/tickets')
  await expect(page.getByRole('link', { name: /^Jane Smith/ })).toBeVisible() // a card is one link
  await expect(page.locator('table')).toBeHidden()
  await expectNoSidewaysScroll(page)

  await page.goto('/tickets/b2a1f7c0-2a93-4d8f-a23e-9b1a24dcf5a1')
  await expect(page.getByRole('heading', { level: 1, name: 'John Doe' })).toBeVisible()
  await expectNoSidewaysScroll(page)
})

test('phone: the new-ticket form fills the screen and works', async ({ page }) => {
  await page.goto('/tickets')
  await page.getByRole('button', { name: 'New ticket', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: 'New ticket' })
  const box = await dialog.boundingBox()
  expect(box?.width).toBe(375) // full-screen sheet, not a cramped card

  const name = uniqueName('Phone')
  await dialog.getByLabel('Name').fill(name)
  await dialog.getByLabel('Email address').fill('phone.user@example.com')
  await dialog.getByLabel('Description').fill('The app crashes when I open the camera.')
  await dialog.getByRole('button', { name: 'Submit', exact: true }).click()

  await expect(page.getByRole('heading', { level: 1, name })).toBeVisible()
  await expectNoSidewaysScroll(page)
})

test('phone: an admin edits a card and saves it from the bar at the bottom', async ({ page, request }) => {
  const ticket = await createTicket(request, uniqueName('Card'))
  await logIn(page)
  await page.goto(`/tickets?search=${encodeURIComponent(ticket.name)}`)
  const card = page.locator('li').filter({ hasText: ticket.name })

  await card.getByLabel('Status').selectOption('Resolved')
  await card.getByLabel('Resolution').fill('Reinstalled the app.')
  const bar = page.getByRole('region', { name: 'Unsaved changes' })
  await expect(bar).toBeVisible()
  // The bar sits at the bottom edge of the screen, so Save is reachable without scrolling.
  const barBox = await bar.boundingBox()
  expect(Math.round((barBox?.y ?? 0) + (barBox?.height ?? 0))).toBe(page.viewportSize()?.height)
  await expectNoSidewaysScroll(page)

  await bar.getByRole('button', { name: 'Save changes', exact: true }).click()
  await expect(page.getByRole('status').filter({ hasText: 'Saved 1 ticket' })).toBeVisible()
  await page.reload()
  await expect(card.getByLabel('Status')).toHaveValue('Resolved')
})
