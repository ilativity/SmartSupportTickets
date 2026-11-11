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
  await expect(rows.first()).toBeVisible()
  for (const status of await rows.locator('td:nth-child(4)').allInnerTexts()) {
    expect(status.trim()).toBe('In Progress')
  }

  await page.getByLabel('Status').selectOption('')
  await page.getByLabel('Search').fill('laptop')
  await expect(page).toHaveURL(/search=laptop/)
  await expect(page.getByRole('link', { name: 'John Doe', exact: true })).toBeVisible()
  await expect(page.getByRole('link', { name: 'Jane Smith', exact: true })).toHaveCount(0)

  // A reload (or a shared link) shows the same filtered list.
  await page.reload()
  await expect(page.getByLabel('Search')).toHaveValue('laptop')
  await expect(page.getByRole('link', { name: 'John Doe', exact: true })).toBeVisible()
  await expect(page.getByRole('link', { name: 'Jane Smith', exact: true })).toHaveCount(0)
})

test('search with no match shows an empty state, not an error', async ({ page }) => {
  await page.goto('/tickets?search=zz-no-such-ticket')
  await expect(page.getByText('No tickets match these filters.')).toBeVisible()
})

test('Back restores the previous search in the box and the list', async ({ page }) => {
  await page.goto('/tickets')
  await page.getByLabel('Search').fill('laptop')
  await expect(page).toHaveURL(/search=laptop/)
  await page.getByLabel('Status').selectOption('Closed') // a new history entry
  await expect(page).toHaveURL(/status=Closed/)
  await page.getByLabel('Search').fill('air') // typing replaces that entry: ?status=Closed&search=air
  await expect(page).toHaveURL(/search=air/)

  await page.goBack()
  await expect(page).not.toHaveURL(/status=Closed/)
  // The box must follow the URL back to "laptop", not keep showing "air".
  await expect(page.getByLabel('Search')).toHaveValue('laptop')
  await expect(page.getByRole('link', { name: 'John Doe', exact: true })).toBeVisible()
  await expect(page.getByRole('link', { name: 'Emily Johnson', exact: true })).toHaveCount(0)
})
