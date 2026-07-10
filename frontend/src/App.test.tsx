import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import App from './App'

const emptyCart = { id: 'cart-1', items: [], subtotal: 0, currency: null, version: 0, createdAt: '', updatedAt: '' }

describe('App', () => {
  beforeEach(() => { localStorage.clear(); vi.restoreAllMocks() })

  it('creates a cart and lets the user add a product', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(JSON.stringify({ cart: emptyCart, accessToken: 'token' }), { status: 201 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ ...emptyCart, version: 1, subtotal: 149, currency: 'EUR', items: [{ productId: '10000000-0000-0000-0000-000000000001', name: 'Orbit headphones', unitPrice: 149, quantity: 1, lineTotal: 149 }] }), { status: 200 }))
    render(<App />)
    await waitFor(() => expect(screen.getByText('Nothing here yet.')).toBeInTheDocument())
    await userEvent.click(screen.getAllByRole('button', { name: /add to cart/i })[0])
    expect((await screen.findAllByText('€149.00')).length).toBeGreaterThanOrEqual(2)
    expect(fetchMock).toHaveBeenCalledTimes(2)
  })

  it('shows a retry action when the API is unavailable', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new Error('offline'))
    render(<App />)
    expect(await screen.findByRole('alert')).toHaveTextContent('unavailable')
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
  })
})
