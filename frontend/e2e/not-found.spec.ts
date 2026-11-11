import { expect, test } from '@playwright/test'

// Spec 6: unknown addresses get a not-found page, not a crash or an endless spinner.
test('an unknown ticket id shows "ticket does not exist"', async ({ page }) => {
  await page.goto('/tickets/00000000-0000-0000-0000-000000000000')
  await expect(page.getByRole('heading', { name: 'This ticket does not exist' })).toBeVisible()
  await page.getByRole('link', { name: 'All tickets', exact: true }).click()
  await expect(page).toHaveURL(/\/tickets$/)
})

test('a malformed ticket id is also "not found"', async ({ page }) => {
  await page.goto('/tickets/not-a-real-id')
  await expect(page.getByRole('heading', { name: 'This ticket does not exist' })).toBeVisible()
})

test('an unknown page shows the generic not-found page', async ({ page }) => {
  await page.goto('/no/such/page')
  await expect(page.getByRole('heading', { name: 'This page does not exist' })).toBeVisible()
})
