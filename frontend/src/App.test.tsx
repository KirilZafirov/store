import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from './App'
import type { Cart } from './types'

const now = '2026-07-10T12:00:00Z'
const emptyCart: Cart = { id: 'cart-1', items: [], subtotal: 0, currency: null, version: 0, createdAt: now, updatedAt: now }
const orbit = { productId: '10000000-0000-0000-0000-000000000001', name: 'Orbit headphones', unitPrice: 149, quantity: 1, lineTotal: 149 }
const cartWithOrbit = { ...emptyCart, version: 1, subtotal: 149, currency: 'EUR', items: [orbit] }
const cartWithTwoOrbits = { ...emptyCart, version: 2, subtotal: 298, currency: 'EUR', items: [{ ...orbit, quantity: 2, lineTotal: 298 }] }
const conflictProblem = { type: 'https://atlas-cart.dev/problems/concurrency_conflict', title: 'Conflict', status: 409, detail: 'The cart changed.', code: 'concurrency_conflict' }
const idempotencyProblem = { type: 'https://atlas-cart.dev/problems/idempotency_key_reused', title: 'Conflict', status: 409, detail: 'Idempotency key reused.', code: 'idempotency_key_reused' }
const validationProblem = { type: 'https://atlas-cart.dev/problems/validation', title: 'Bad Request', status: 400, detail: 'The request is invalid.', code: 'validation_failed', errors: { quantity: ['Quantity must be positive.'] } }
const firstKey = 'aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa'
const secondKey = 'bbbbbbbb-bbbb-4bbb-bbbb-bbbbbbbbbbbb'

const createdResponse = (cart = emptyCart) => new Response(JSON.stringify({ cart, accessToken: 'token' }), { status: 201 })
const cartResponse = (cart = emptyCart) => new Response(JSON.stringify(cart), { status: 200 })
const problemResponse = (problem: object, status = 400) => new Response(JSON.stringify(problem), { status })

function seedStoredCart() {
  localStorage.setItem('atlas.cart.id', emptyCart.id)
  localStorage.setItem('atlas.cart.token', 'token')
}

function mockFetch(...responses: Array<Response | Error>) {
  const fetchMock = vi.spyOn(globalThis, 'fetch')
  for (const response of responses) {
    if (response instanceof Error) fetchMock.mockRejectedValueOnce(response)
    else fetchMock.mockResolvedValueOnce(response)
  }
  return fetchMock
}

describe('App', () => {
  beforeEach(() => {
    localStorage.clear()
    vi.restoreAllMocks()
  })

  afterEach(() => cleanup())

  it('shows loading and then creates an empty cart', async () => {
    mockFetch(createdResponse())

    render(<App />)

    expect(screen.getByText('Preparing your cart…')).toBeInTheDocument()
    expect(await screen.findByText('Nothing here yet.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /add orbit headphones to cart/i })).toBeEnabled()
  })

  it('restores a stored cart without creating a new one', async () => {
    seedStoredCart()
    const fetchMock = mockFetch(cartResponse(cartWithOrbit))

    render(<App />)

    expect(await screen.findByText('Orbit headphones')).toBeInTheDocument()
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(String(fetchMock.mock.calls[0][0])).toContain(`/api/v1/carts/${emptyCart.id}`)
    expect(fetchMock.mock.calls[0][1]?.method).toBeUndefined()
  })

  it('creates a cart and lets the user add a product with an idempotency key', async () => {
    vi.spyOn(crypto, 'randomUUID').mockReturnValue(firstKey)
    const fetchMock = mockFetch(createdResponse(), cartResponse(cartWithOrbit))

    render(<App />)
    await screen.findByText('Nothing here yet.')

    await userEvent.click(screen.getByRole('button', { name: /add orbit headphones to cart/i }))

    expect((await screen.findAllByText('€149.00')).length).toBeGreaterThanOrEqual(2)
    expect(new Headers(fetchMock.mock.calls[1][1]?.headers).get('Idempotency-Key')).toBe(firstKey)
  })

  it('supports keyboard quantity editing, removal, and clearing', async () => {
    seedStoredCart()
    vi.spyOn(crypto, 'randomUUID').mockReturnValue(firstKey)
    const fetchMock = mockFetch(
      cartResponse(cartWithOrbit),
      cartResponse(cartWithTwoOrbits),
      cartResponse(cartWithOrbit),
      cartResponse(emptyCart),
      cartResponse(cartWithOrbit),
      cartResponse(emptyCart)
    )

    render(<App />)
    await screen.findByText('Orbit headphones')

    await userEvent.tab()
    await userEvent.keyboard('{Enter}')
    await userEvent.click(screen.getByRole('button', { name: /increase orbit headphones quantity/i }))
    await waitFor(() => expect(screen.getByLabelText(/quantity for orbit headphones/i)).toHaveTextContent('2'))

    await userEvent.click(screen.getByRole('button', { name: /decrease orbit headphones quantity/i }))
    await waitFor(() => expect(screen.getByLabelText(/quantity for orbit headphones/i)).toHaveTextContent('1'))

    await userEvent.click(screen.getByRole('button', { name: /remove orbit headphones/i }))
    expect(await screen.findByText('Nothing here yet.')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: /add orbit headphones to cart/i }))
    await waitFor(() => expect(screen.getByLabelText(/quantity for orbit headphones/i)).toHaveTextContent('1'))
    await userEvent.click(screen.getByRole('button', { name: /clear cart/i }))
    expect(await screen.findByText('Choose an essential above to begin.')).toBeInTheDocument()

    expect(fetchMock).toHaveBeenCalledTimes(6)
    expect(fetchMock.mock.calls.map((call) => call[1]?.method)).toEqual([undefined, 'PUT', 'PUT', 'DELETE', 'POST', 'DELETE'])
  })

  it('shows validation details without offering a mutation retry', async () => {
    vi.spyOn(crypto, 'randomUUID').mockReturnValue(firstKey)
    mockFetch(createdResponse(), problemResponse(validationProblem, 400))

    render(<App />)
    await screen.findByText('Nothing here yet.')
    await userEvent.click(screen.getByRole('button', { name: /add orbit headphones to cart/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent('The request is invalid.')
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /retry add orbit headphones/i })).not.toBeInTheDocument()
  })

  it('shows a retry action when the API is unavailable during startup', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new Error('offline'))

    render(<App />)

    expect(await screen.findByRole('alert')).toHaveTextContent('unavailable')
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
  })

  it('retries an unknown mutation outcome with the same idempotency key', async () => {
    vi.spyOn(crypto, 'randomUUID').mockReturnValue(firstKey)
    const fetchMock = mockFetch(createdResponse(), new Error('connection dropped'), cartResponse(cartWithOrbit))

    render(<App />)
    await screen.findByText('Nothing here yet.')

    await userEvent.click(screen.getByRole('button', { name: /add orbit headphones to cart/i }))
    expect(await screen.findByRole('alert')).toHaveTextContent('unavailable')
    await userEvent.click(screen.getByRole('button', { name: /retry add orbit headphones/i }))

    expect((await screen.findAllByText('€149.00')).length).toBeGreaterThanOrEqual(2)
    expect(new Headers(fetchMock.mock.calls[1][1]?.headers).get('Idempotency-Key')).toBe(firstKey)
    expect(new Headers(fetchMock.mock.calls[2][1]?.headers).get('Idempotency-Key')).toBe(firstKey)
  })

  it('refreshes after a conflict and retries the user action with a new key and version', async () => {
    vi.spyOn(crypto, 'randomUUID')
      .mockReturnValueOnce(firstKey)
      .mockReturnValueOnce(secondKey)
    const refreshedCart = { ...emptyCart, version: 7 }
    const fetchMock = mockFetch(
      createdResponse(),
      problemResponse(conflictProblem, 409),
      cartResponse(refreshedCart),
      cartResponse({ ...cartWithOrbit, version: 8 })
    )

    render(<App />)
    await screen.findByText('Nothing here yet.')

    await userEvent.click(screen.getByRole('button', { name: /add orbit headphones to cart/i }))
    expect(await screen.findByRole('alert')).toHaveTextContent('refreshed')
    await userEvent.click(screen.getByRole('button', { name: /retry add orbit headphones/i }))

    expect((await screen.findAllByText('€149.00')).length).toBeGreaterThanOrEqual(2)
    expect(new Headers(fetchMock.mock.calls[1][1]?.headers).get('Idempotency-Key')).toBe(firstKey)
    expect(new Headers(fetchMock.mock.calls[3][1]?.headers).get('Idempotency-Key')).toBe(secondKey)
    expect(JSON.parse(String(fetchMock.mock.calls[3][1]?.body))).toMatchObject({ version: 7 })
  })

  it('handles refresh failure after a conflict without leaving a pending retry', async () => {
    vi.spyOn(crypto, 'randomUUID').mockReturnValue(firstKey)
    mockFetch(createdResponse(), problemResponse(conflictProblem, 409), new Error('offline'))

    render(<App />)
    await screen.findByText('Nothing here yet.')

    await userEvent.click(screen.getByRole('button', { name: /add orbit headphones to cart/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent('refresh failed')
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /retry add orbit headphones/i })).not.toBeInTheDocument()
  })

  it('does not retry a reused idempotency key with a different request', async () => {
    vi.spyOn(crypto, 'randomUUID').mockReturnValue(firstKey)
    mockFetch(createdResponse(), problemResponse(idempotencyProblem, 409))

    render(<App />)
    await screen.findByText('Nothing here yet.')

    await userEvent.click(screen.getByRole('button', { name: /add orbit headphones to cart/i }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('already used')
    expect(within(alert).getByRole('button', { name: 'Retry' })).toBeInTheDocument()
  })
})
