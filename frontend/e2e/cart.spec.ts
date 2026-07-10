import { expect, test } from '@playwright/test'

const apiUrl = process.env.E2E_API_URL ?? 'http://localhost:8080'

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => localStorage.clear())
  await page.goto('/')
})

test('creates, adds repeatedly, updates, removes, and clears a cart', async ({ page }) => {
  await expect(page.getByText('Nothing here yet.')).toBeVisible()

  await page.getByRole('button', { name: /add orbit headphones to cart/i }).click()
  await expect(page.getByLabel(/quantity for orbit headphones/i)).toContainText('1')

  await page.getByRole('button', { name: /add orbit headphones to cart/i }).click()
  await expect(page.getByLabel(/quantity for orbit headphones/i)).toContainText('2')

  await page.getByRole('button', { name: /increase orbit headphones quantity/i }).click()
  await expect(page.getByLabel(/quantity for orbit headphones/i)).toContainText('3')

  await page.getByRole('button', { name: /decrease orbit headphones quantity/i }).click()
  await expect(page.getByLabel(/quantity for orbit headphones/i)).toContainText('2')

  await page.getByRole('button', { name: /remove orbit headphones/i }).click()
  await expect(page.getByText('Nothing here yet.')).toBeVisible()

  await page.getByRole('button', { name: /add contour keyboard to cart/i }).click()
  await expect(page.getByText('Contour keyboard')).toBeVisible()
  await page.getByRole('button', { name: /clear cart/i }).click()
  await expect(page.getByText('Choose an essential above to begin.')).toBeVisible()
})

test('recovers from a version conflict by refreshing and retrying with a new key', async ({ page }) => {
  let conflictInjected = false

  await page.route(`${apiUrl}/api/v1/carts/*/items`, async (route) => {
    const request = route.request()
    if (!conflictInjected && request.method() === 'POST') {
      conflictInjected = true
      await route.fulfill({
        status: 409,
        contentType: 'application/problem+json',
        body: JSON.stringify({
          type: 'https://atlas-cart.dev/problems/concurrency_conflict',
          title: 'Conflict',
          status: 409,
          detail: 'The cart changed.',
          code: 'concurrency_conflict'
        })
      })
      return
    }

    await route.continue()
  })

  await expect(page.getByText('Nothing here yet.')).toBeVisible()
  await page.getByRole('button', { name: /add orbit headphones to cart/i }).click()

  await expect(page.getByRole('alert')).toContainText('refreshed')
  await page.getByRole('button', { name: /retry add orbit headphones/i }).click()

  await expect(page.getByLabel(/quantity for orbit headphones/i)).toContainText('1')
})

test('shows an unavailable-service state when the API cannot create a cart', async ({ page }) => {
  await page.route(`${apiUrl}/api/v1/carts`, async (route) => {
    if (route.request().method() === 'POST') {
      await route.abort()
      return
    }

    await route.continue()
  })

  await page.reload()

  await expect(page.getByRole('alert')).toContainText('unavailable')
  await expect(page.getByRole('button', { name: 'Retry' })).toBeVisible()
})
