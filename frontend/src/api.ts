import type { Cart, CreatedCart, Product } from './types'

const API_URL = import.meta.env.VITE_API_URL ?? 'http://localhost:8080'

export class ApiError extends Error {
  constructor(public status: number, message: string) { super(message) }
}

async function call<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response
  try { response = await fetch(`${API_URL}${path}`, init) }
  catch { throw new ApiError(0, 'The cart service is unavailable. Check the API and try again.') }
  if (!response.ok) {
    const problem = await response.json().catch(() => ({})) as { detail?: string }
    throw new ApiError(response.status, problem.detail ?? 'The request could not be completed.')
  }
  return response.json() as Promise<T>
}

const headers = (token: string, mutation = false): HeadersInit => ({
  'Content-Type': 'application/json', 'X-Cart-Token': token,
  ...(mutation ? { 'Idempotency-Key': crypto.randomUUID() } : {})
})

export const api = {
  create: () => call<CreatedCart>('/api/v1/carts', { method: 'POST' }),
  get: (id: string, token: string) => call<Cart>(`/api/v1/carts/${id}`, { headers: headers(token) }),
  add: (cart: Cart, token: string, product: Product) => call<Cart>(`/api/v1/carts/${cart.id}/items`, {
    method: 'POST', headers: headers(token, true), body: JSON.stringify({ productId: product.id, name: product.name, unitPrice: product.price, currency: 'EUR', quantity: 1, version: cart.version })
  }),
  quantity: (cart: Cart, token: string, productId: string, quantity: number) => call<Cart>(`/api/v1/carts/${cart.id}/items/${productId}`, {
    method: 'PUT', headers: headers(token, true), body: JSON.stringify({ quantity, version: cart.version })
  }),
  remove: (cart: Cart, token: string, productId: string) => call<Cart>(`/api/v1/carts/${cart.id}/items/${productId}?version=${cart.version}`, { method: 'DELETE', headers: headers(token, true) }),
  clear: (cart: Cart, token: string) => call<Cart>(`/api/v1/carts/${cart.id}/items?version=${cart.version}`, { method: 'DELETE', headers: headers(token, true) })
}
