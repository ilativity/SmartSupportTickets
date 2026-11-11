import { expect, test } from '@playwright/test'

// Spec 1: list, status filter and search, kept in the URL.
test('lists tickets, filters by status and searches, all reflected in the URL', async ({ page }) => {
  await page.goto('/')
  await expect(page).toHaveURL(/\/tickets$/)
  const rows = page.locator('tbody tr')
  await expect(page.getByRole('link', { name: 'Jane Smith', exact: true })).toBeVisible()
  // The public list never shows customer emails.
  await expect(page.locator('main')).not.toContainText('@example.com')

  await page.getByLabel('Status').selectOption('In Progress')
  await expect(page).toHaveURL(/status=In\+Progress|status=In%20Progress/)
  // Retrying assertions: the old list stays on screen until the filtered one arrives (no flicker), so a one-off
  // read right after the URL changes could still see the previous rows. That race made this spec flaky.
  await expect(rows.first()).toBeVisible()
  await expect(rows.locator('td:nth-child(4)').filter({ hasNotText: 'In Progress' })).toHaveCount(0)

  await page.getByLabel('Status').selectOption('')
  await page.getByLabel('Search', { exact: true }).fill('laptop')
  await expect(page).toHaveURL(/search=laptop/)
  await expect(page.getByRole('link', { name: 'John Doe', exact: true })).toBeVisible()
  await expect(page.getByRole('link', { name: 'Jane Smith', exact: true })).toHaveCount(0)

  // A reload (or a shared link) shows the same filtered list.
  await page.reload()
  await expect(page.getByLabel('Search', { exact: true })).toHaveValue('laptop')
  await expect(page.getByRole('link', { name: 'John Doe', exact: true })).toBeVisible()
  await expect(page.getByRole('link', { name: 'Jane Smith', exact: true })).toHaveCount(0)
})

test('the clear button empties the search, the URL and the filter, and keeps focus in the box', async ({ page }) => {
  await page.goto('/tickets?search=laptop')
  const clear = page.getByRole('button', { name: 'Clear search', exact: true })
  await expect(page.getByRole('link', { name: 'Jane Smith', exact: true })).toHaveCount(0)

  await clear.click()

  await expect(page.getByLabel('Search', { exact: true })).toHaveValue('')
  await expect(page.getByLabel('Search', { exact: true })).toBeFocused()
  await expect(page).not.toHaveURL(/search=/)
  await expect(page.getByRole('link', { name: 'Jane Smith', exact: true })).toBeVisible()
  await expect(clear).toHaveCount(0) // nothing left to clear
})

test('search with no match shows an empty state, not an error', async ({ page }) => {
  await page.goto('/tickets?search=zz-no-such-ticket')
  await expect(page.getByText('No tickets match these filters.')).toBeVisible()
})

test('Back restores the previous search in the box and the list', async ({ page }) => {
  await page.goto('/tickets')
  await page.getByLabel('Search', { exact: true }).fill('laptop')
  await expect(page).toHaveURL(/search=laptop/)
  await page.getByLabel('Status').selectOption('Closed') // a new history entry
  await expect(page).toHaveURL(/status=Closed/)
  await page.getByLabel('Search', { exact: true }).fill('air') // typing replaces that entry: ?status=Closed&search=air
  await expect(page).toHaveURL(/search=air/)

  await page.goBack()
  await expect(page).not.toHaveURL(/status=Closed/)
  // The box must follow the URL back to "laptop", not keep showing "air".
  await expect(page.getByLabel('Search', { exact: true })).toHaveValue('laptop')
  await expect(page.getByRole('link', { name: 'John Doe', exact: true })).toBeVisible()
  await expect(page.getByRole('link', { name: 'Emily Johnson', exact: true })).toHaveCount(0)
})

test('the table shows the full description; a long one expands in place without opening the ticket', async ({ page, request }) => {
  const ending = 'and this is the very end of a long description.'
  const description = `${'The printer prints faint lines on every page after the cartridge change. '.repeat(4)}${ending}`
  const created = await request.post('/api/tickets', {
    multipart: { name: `Long ${Date.now().toString(36)}`, email: 'long@example.com', description },
  })
  const { name } = (await created.json()) as { name: string }
  await page.goto(`/tickets?search=${encodeURIComponent(name)}`)
  const row = page.locator('tbody tr').filter({ hasText: name })
  const more = row.getByRole('button', { name: 'Show more', exact: true })

  // Geometry, not text presence: a clamped paragraph still CONTAINS the whole text, it just does not show it.
  const text = row.getByText(ending)
  const clipped = () => text.evaluate((p) => p.scrollHeight > p.clientHeight + 1)
  await expect(more).toHaveAttribute('aria-expanded', 'false')
  expect(await clipped()).toBe(true)

  await more.click()

  await expect(page).toHaveURL(/\/tickets\?/) // still on the list
  await expect.poll(clipped).toBe(false)
  await expect(row.getByRole('button', { name: 'Show less', exact: true })).toHaveAttribute('aria-expanded', 'true')
})
