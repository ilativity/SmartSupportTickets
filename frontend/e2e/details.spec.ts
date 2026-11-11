import { expect, test } from '@playwright/test'
import { JOHN, createTicket, logIn, uniqueName } from './helpers'

// Spec 3: a visitor sees the ticket read-only with a masked email.
test('anonymous details: masked email, no edit controls, a way to log in', async ({ page }) => {
  await page.goto(`/tickets/${JOHN.id}`)
  await expect(page.getByRole('heading', { level: 1, name: JOHN.name })).toBeVisible()
  await expect(page.getByText('j***@example.com')).toBeVisible()
  await expect(page.locator('main')).not.toContainText(JOHN.email)
  await expect(page.getByRole('heading', { name: 'Manage ticket' })).toHaveCount(0)
  await expect(page.getByRole('link', { name: 'Log in to edit', exact: true })).toBeVisible()
  // The dataset's image file does not exist: a placeholder, not a broken image.
  await expect(page.getByText('Image not available')).toBeVisible()
})

// Spec 4: admin edits on the detail page; the change is persisted.
test('admin edits status and resolution on the detail page; it survives a reload', async ({ page, request }) => {
  const ticket = await createTicket(request, uniqueName('Edit'))
  await page.goto(`/tickets/${ticket.id}`)
  await page.getByRole('link', { name: 'Log in to edit', exact: true }).click()
  await page.getByLabel('Username').fill('admin')
  await page.getByLabel('Password').fill('Admin123!')
  await page.getByRole('button', { name: 'Log in', exact: true }).click()

  // Back on the same ticket, now as admin: full email and the edit form.
  await expect(page).toHaveURL(new RegExp(`/tickets/${ticket.id}$`))
  await expect(page.getByText('e2e.customer@example.com')).toBeVisible()
  const save = page.getByRole('button', { name: 'Save', exact: true })
  await expect(save).toBeDisabled() // nothing changed yet

  await page.getByLabel('Status').selectOption('Resolved')
  await page.getByLabel('Resolution').fill('Replaced the paper feed roller.')
  await expect(save).toBeEnabled()
  await save.click()
  await expect(page.getByRole('status').filter({ hasText: 'Saved' })).toBeVisible()

  await page.reload()
  await expect(page.getByLabel('Status')).toHaveValue('Resolved')
  await expect(page.getByLabel('Resolution')).toHaveValue('Replaced the paper feed roller.')

  // Logging out hides the full email again.
  await page.getByRole('button', { name: 'Log out', exact: true }).click()
  await expect(page.getByText('e***@example.com')).toBeVisible()
  await expect(page.locator('main')).not.toContainText('e2e.customer@example.com')
})

test('an admin session survives a reload', async ({ page }) => {
  await logIn(page)
  await page.reload()
  await expect(page.getByRole('button', { name: 'Log out', exact: true })).toBeVisible()
})
