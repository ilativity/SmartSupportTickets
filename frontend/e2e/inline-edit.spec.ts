import { expect, test, type Page } from '@playwright/test'
import { createTicket, logIn, uniqueName } from './helpers'

const field = (page: Page, what: 'Status' | 'Resolution', name: string) =>
  page.locator('table').getByLabel(new RegExp(`^${what} of ${name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}'s ticket #`))

const bar = (page: Page) => page.getByRole('region', { name: 'Unsaved changes' })

async function adminListFor(page: Page, search: string) {
  await logIn(page)
  await page.goto(`/tickets?search=${encodeURIComponent(search)}`)
}

// Spec 4b: the admin edits two rows inline in the table (as in the assignment's mockup) and saves both at once.
test('admin edits two rows inline and saves both with one click', async ({ page, request }) => {
  const prefix = uniqueName('Inline')
  const first = await createTicket(request, `${prefix} A`)
  const second = await createTicket(request, `${prefix} B`)
  await adminListFor(page, prefix)
  await expect(field(page, 'Status', first.name)).toBeVisible()
  await expect(bar(page)).toHaveCount(0) // nothing changed yet

  // A real pointer click on the select: proves the row's link overlay does not swallow it.
  await field(page, 'Status', first.name).click()
  await field(page, 'Status', first.name).selectOption('In Progress')
  await field(page, 'Resolution', second.name).fill('Toner replaced.')
  await expect(bar(page)).toContainText('2 unsaved changes')

  await bar(page).getByRole('button', { name: 'Save changes', exact: true }).click()
  await expect(page.getByRole('status').filter({ hasText: 'Saved 2 tickets' })).toBeVisible()
  await expect(bar(page)).toHaveCount(0)
  // The refreshed rows already show the saved values (no flash back to the old ones).
  await expect(field(page, 'Status', first.name)).toHaveValue('In Progress')

  await page.reload()
  await expect(field(page, 'Status', first.name)).toHaveValue('In Progress')
  await expect(field(page, 'Resolution', second.name)).toHaveValue('Toner replaced.')
})

test('typing keeps every character, including a trailing space; going back to the saved value is no change', async ({ page, request }) => {
  const ticket = await createTicket(request, uniqueName('Typing'))
  await adminListFor(page, ticket.name)
  const resolution = field(page, 'Resolution', ticket.name)

  await resolution.pressSequentially('Fixed ')
  await expect(resolution).toHaveValue('Fixed ')
  await resolution.pressSequentially('again')
  await expect(resolution).toHaveValue('Fixed again')
  await expect(bar(page)).toContainText('1 unsaved change')

  await resolution.fill('')
  await expect(bar(page)).toHaveCount(0)
  await field(page, 'Status', ticket.name).selectOption('Closed')
  await expect(bar(page)).toContainText('1 unsaved change')
  await field(page, 'Status', ticket.name).selectOption('New')
  await expect(bar(page)).toHaveCount(0)
})

test('a row that fails to save stays marked with its reason; the other row is saved', async ({ page, request }) => {
  const prefix = uniqueName('Partial')
  const ok = await createTicket(request, `${prefix} ok`)
  const broken = await createTicket(request, `${prefix} broken`)
  await page.route(`**/api/tickets/${broken.id}`, (route) =>
    route.request().method() === 'PUT'
      ? route.fulfill({ status: 500, contentType: 'application/problem+json', body: JSON.stringify({ status: 500, title: 'Server error' }) })
      : route.fallback(),
  )
  await adminListFor(page, prefix)

  await field(page, 'Status', ok.name).selectOption('Resolved')
  await field(page, 'Status', broken.name).selectOption('Resolved')
  await bar(page).getByRole('button', { name: 'Save changes', exact: true }).click()

  await expect(page.getByRole('status').filter({ hasText: '1 could not be saved' })).toBeVisible()
  await expect(page.locator('table').getByRole('alert')).toContainText('Something went wrong on the server')
  await expect(bar(page)).toContainText('1 unsaved change')
  await expect(field(page, 'Status', broken.name)).toHaveValue('Resolved') // the admin's edit is kept
})

test('edits in rows hidden by the filter are not saved unseen', async ({ page, request }) => {
  const prefix = uniqueName('Hidden')
  const ticket = await createTicket(request, `${prefix} one`)
  await adminListFor(page, prefix)
  await field(page, 'Status', ticket.name).selectOption('Closed')

  await page.getByLabel('Search', { exact: true }).fill('zz-nothing-matches')
  await expect(bar(page)).toContainText('1 more hidden by the filter')
  await expect(bar(page).getByRole('button', { name: 'Save changes', exact: true })).toBeDisabled()

  await page.getByLabel('Search', { exact: true }).fill(prefix)
  await expect(bar(page)).toContainText('1 unsaved change')
})

test('Undo on one row discards only that row', async ({ page, request }) => {
  const prefix = uniqueName('Undo')
  const keep = await createTicket(request, `${prefix} keep`)
  const drop = await createTicket(request, `${prefix} drop`)
  await adminListFor(page, prefix)

  await field(page, 'Status', keep.name).selectOption('Closed')
  await field(page, 'Status', drop.name).selectOption('Closed')
  await expect(bar(page)).toContainText('2 unsaved changes')

  await page.locator('table').getByRole('button', { name: `Undo for ${drop.name}'s ticket`, exact: true }).click()

  await expect(field(page, 'Status', drop.name)).toHaveValue('New')
  await expect(field(page, 'Status', keep.name)).toHaveValue('Closed')
  await expect(bar(page)).toContainText('1 unsaved change')
})

test('visitors see the list read-only', async ({ page }) => {
  await page.goto('/tickets')
  await expect(page.locator('table').getByRole('link').first()).toBeVisible() // rows are there
  await expect(page.locator('table select')).toHaveCount(0)
  await expect(page.locator('table textarea')).toHaveCount(0)
})
