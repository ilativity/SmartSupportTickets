import { expect, test } from '@playwright/test'

// Spec 5: login failures are told apart correctly.
test('a wrong password shows "invalid username or password"', async ({ page }) => {
  await page.goto('/login')
  await page.getByLabel('Username').fill('admin')
  await page.getByLabel('Password').fill('not-the-password')
  await page.getByRole('button', { name: 'Log in', exact: true }).click()
  await expect(page.getByRole('alert')).toHaveText('Invalid username or password.')
  await expect(page).toHaveURL(/\/login$/)
})

test('a rate-limited login says "too many attempts", never "wrong password"', async ({ page }) => {
  // Stubbed: the real limit is raised in the E2E environment so parallel specs can log in.
  await page.route('**/api/auth/login', (route) =>
    route.fulfill({
      status: 429,
      contentType: 'application/problem+json',
      headers: { 'Retry-After': '60' },
      body: JSON.stringify({ status: 429, title: 'Too many login attempts', detail: 'Try again in a minute.' }),
    }),
  )
  await page.goto('/login')
  await page.getByLabel('Username').fill('admin')
  await page.getByLabel('Password').fill('Admin123!')
  await page.getByRole('button', { name: 'Log in', exact: true }).click()
  await expect(page.getByRole('alert')).toContainText('Too many attempts')
  await expect(page.getByRole('alert')).not.toContainText(/invalid|wrong/i)
})
