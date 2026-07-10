import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from './App'

const emptyCart = { id: 'cart-1', items: [], subtotal: 0, currency: null, version: 0, createdAt: '', updatedAt: '' }
const cartWithOrbit = { ...emptyCart, version: 1, subtotal: 149, currency: 'EUR', items: [{ productId: '10000000-0000-0000-0000-000000000001', name: 'Orbit headphones', unitPrice: 149, quantity: 1, lineTotal: 149 }] }
const conflictProblem = { type: 'https://atlas-cart.dev/problems/concurrency_conflict', title: 'Conflict', status: 409, detail: 'The cart changed.', code: 'concurrency_conflict' }
const idempotencyProblem = { type: 'https://atlas-cart.dev/problems/idempotency_key_reused', title: 'Conflict', status: 409, detail: 'Idempotency key reused.', code: 'idempotency_key_reused' }
const firstKey = 'aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa'
const secondKey = 'bbbbbbbb-bbbb-4bbb-bbbb-bbbbbbbbbbbb'

describe('App', () => {
  beforeEach(() => { localStorage.clear(); vi.restoreAllMocks() })
  afterEach(() => cleanup())

  it('creates a cart and lets the user add a product', async () => {
    vi.spyOn(crypto, 'randomUUID').mockReturnValue(firstKey)
    const fetchMock = vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(JSON.stringify({ cart: emptyCart, accessToken: 'token' }), { status: 201 }))
      .mockResolvedValueOnce(new Response(JSON.stringify(cartWithOrbit), { status: 200 }))
    render(<App />)
    await waitFor(() => expect(screen.getByText('Nothing here yet.')).toBeInTheDocument())
    await userEvent.click(screen.getAllByRole('button', { name: /add to cart/i })[0])
    expect((await screen.findAllByText('€149.00')).length).toBeGreaterThanOrEqual(2)
    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(new Headers(fetchMock.mock.calls[1][1]?.headers).get('Idempotency-Key')).toBe(firstKey)
  })

  it('shows a retry action when the API is unavailable', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new Error('offline'))
    render(<App />)
    expect(await screen.findByRole('alert')).toHaveTextContent('unavailable')
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
  })

  it('retries an unknown mutation outcome with the same idempotency key', async () => {
    vi.spyOn(crypto, 'randomUUID').mockReturnValue(firstKey)
    const fetchMock = vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(JSON.stringify({ cart: emptyCart, accessToken: 'token' }), { status: 201 }))
      .mockRejectedValueOnce(new Error('connection dropped'))
      .mockResolvedValueOnce(new Response(JSON.stringify(cartWithOrbit), { status: 200 }))
    render(<App />)
    await waitFor(() => expect(screen.getByText('Nothing here yet.')).toBeInTheDocument())

    await userEvent.click(screen.getAllByRole('button', { name: /add to cart/i })[0])
    expect(await screen.findByRole('alert')).toHaveTextContent('unavailable')
    await userEvent.click(screen.getByRole('button', { name: /retry add orbit headphones/i }))

    expect((await screen.findAllByText('€149.00')).length).toBeGreaterThanOrEqual(2)
    expect(new Headers(fetchMock.mock.calls[1][1]?.headers).get('Idempotency-Key')).toBe(firstKey)
    expect(new Headers(fetchMock.mock.calls[2][1]?.headers).get('Idempotency-Key')).toBe(firstKey)
  })

  it('refreshes after a conflict and retries the user action with a new key', async () => {
    vi.spyOn(crypto, 'randomUUID')
      .mockReturnValueOnce(firstKey)
      .mockReturnValueOnce(secondKey)
    const refreshedCart = { ...emptyCart, version: 7 }
    const fetchMock = vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(JSON.stringify({ cart: emptyCart, accessToken: 'token' }), { status: 201 }))
      .mockResolvedValueOnce(new Response(JSON.stringify(conflictProblem), { status: 409 }))
      .mockResolvedValueOnce(new Response(JSON.stringify(refreshedCart), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ ...cartWithOrbit, version: 8 }), { status: 200 }))
    render(<App />)
    await waitFor(() => expect(screen.getByText('Nothing here yet.')).toBeInTheDocument())

    await userEvent.click(screen.getAllByRole('button', { name: /add to cart/i })[0])
    expect(await screen.findByRole('alert')).toHaveTextContent('refreshed')
    await userEvent.click(screen.getByRole('button', { name: /retry add orbit headphones/i }))

    expect((await screen.findAllByText('€149.00')).length).toBeGreaterThanOrEqual(2)
    expect(new Headers(fetchMock.mock.calls[1][1]?.headers).get('Idempotency-Key')).toBe(firstKey)
    expect(new Headers(fetchMock.mock.calls[3][1]?.headers).get('Idempotency-Key')).toBe(secondKey)
    expect(JSON.parse(String(fetchMock.mock.calls[3][1]?.body))).toMatchObject({ version: 7 })
  })

  it('does not retry a reused idempotency key with a different request', async () => {
    vi.spyOn(crypto, 'randomUUID').mockReturnValue(firstKey)
    vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(JSON.stringify({ cart: emptyCart, accessToken: 'token' }), { status: 201 }))
      .mockResolvedValueOnce(new Response(JSON.stringify(idempotencyProblem), { status: 409 }))
    render(<App />)
    await waitFor(() => expect(screen.getByText('Nothing here yet.')).toBeInTheDocument())

    await userEvent.click(screen.getAllByRole('button', { name: /add to cart/i })[0])

    expect(await screen.findByRole('alert')).toHaveTextContent('already used')
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
  })
})
