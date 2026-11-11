import { expect, test } from '@playwright/test'

// Spec 8: the API is unreachable when the list loads (stubbed: the request is aborted); Retry recovers.
test('when the API is down the list says so and Retry loads it once it is back', async ({ page }) => {
  let down = true
  await page.route('**/api/tickets?**', (route) => (down ? route.abort('connectionrefused') : route.fallback()))
  await page.route('**/api/tickets', (route) => (down ? route.abort('connectionrefused') : route.fallback()))

  await page.goto('/tickets')
  await expect(page.getByRole('alert')).toContainText('Cannot reach the server')
  const retry = page.getByRole('button', { name: 'Retry', exact: true })
  await expect(retry).toBeVisible()

  down = false
  await retry.click()
  await expect(page.getByRole('link', { name: 'Jane Smith', exact: true })).toBeVisible()
  await expect(page.getByRole('alert')).toHaveCount(0)
})

test('a server error on the ticket page offers Retry, not a blank page', async ({ page }) => {
  let failing = true
  await page.route('**/api/tickets/b2a1f7c0-2a93-4d8f-a23e-9b1a24dcf5a1', (route) =>
    failing
      ? route.fulfill({ status: 503, contentType: 'application/problem+json', body: JSON.stringify({ status: 503, title: 'Unavailable' }) })
      : route.fallback(),
  )

  await page.goto('/tickets/b2a1f7c0-2a93-4d8f-a23e-9b1a24dcf5a1')
  await expect(page.getByRole('alert')).toContainText('Could not load the ticket')
  failing = false
  await page.getByRole('button', { name: 'Retry', exact: true }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'John Doe' })).toBeVisible()
})
