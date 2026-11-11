import { expect, type APIRequestContext, type Page } from '@playwright/test'

export const ADMIN = { username: 'admin', password: 'Admin123!' } // E2E environment's built-in development account

/** Seed ticket from dataset.json: John Doe, laptop, status New. */
export const JOHN = { id: 'b2a1f7c0-2a93-4d8f-a23e-9b1a24dcf5a1', name: 'John Doe', email: 'john.doe@example.com' }

/**
 * Creates a ticket through the API so a test that changes data has a ticket of its own and parallel tests never
 * edit the same one.
 */
export async function createTicket(request: APIRequestContext, name: string) {
  const response = await request.post('/api/tickets', {
    multipart: { name, email: 'e2e.customer@example.com', description: `E2E ticket for ${name}: printer jams on page two.` },
  })
  expect(response.status()).toBe(201)
  return (await response.json()) as { id: string; name: string }
}

/** A name no other test (or run) uses, so searches find exactly this test's ticket. */
export const uniqueName = (prefix: string) => `${prefix} ${Date.now().toString(36)}${Math.floor(Math.random() * 1e4)}`

export async function logIn(page: Page) {
  await page.goto('/login')
  await page.getByLabel('Username').fill(ADMIN.username)
  await page.getByLabel('Password').fill(ADMIN.password)
  await page.getByRole('button', { name: 'Log in', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Log out', exact: true })).toBeVisible()
}
