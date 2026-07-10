import type { Cart, CreatedCart, Product } from './types'

const API_URL = import.meta.env.VITE_API_URL ?? 'http://localhost:8080'

type ProblemDetails = {
  detail?: string
  title?: string
  code?: string
  errors?: Record<string, string[]>
}

export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
    public code?: string,
    public fieldErrors?: Record<string, string[]>
  ) { super(message) }
}

async function call<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response
  try { response = await fetch(`${API_URL}${path}`, init) }
  catch { throw new ApiError(0, 'The cart service is unavailable. Check the API and try again.') }
  if (!response.ok) {
    const problem = await response.json().catch(() => ({})) as ProblemDetails
    throw new ApiError(response.status, problem.detail ?? problem.title ?? 'The request could not be completed.', problem.code, problem.errors)
  }
  return response.json() as Promise<T>
}

const headers = (token: string, idempotencyKey?: string): HeadersInit => ({
  'Content-Type': 'application/json', 'X-Cart-Token': token,
  ...(idempotencyKey ? { 'Idempotency-Key': idempotencyKey } : {})
})

export const api = {
  create: () => call<CreatedCart>('/api/v1/carts', { method: 'POST' }),
  get: (id: string, token: string) => call<Cart>(`/api/v1/carts/${id}`, { headers: headers(token) }),
  add: (cart: Cart, token: string, product: Product, idempotencyKey: string) => call<Cart>(`/api/v1/carts/${cart.id}/items`, {
    method: 'POST', headers: headers(token, idempotencyKey), body: JSON.stringify({ productId: product.id, name: product.name, unitPrice: product.price, currency: product.currency, quantity: 1, version: cart.version })
  }),
  quantity: (cart: Cart, token: string, productId: string, quantity: number, idempotencyKey: string) => call<Cart>(`/api/v1/carts/${cart.id}/items/${productId}`, {
    method: 'PUT', headers: headers(token, idempotencyKey), body: JSON.stringify({ quantity, version: cart.version })
  }),
  remove: (cart: Cart, token: string, productId: string, idempotencyKey: string) => call<Cart>(`/api/v1/carts/${cart.id}/items/${productId}?version=${cart.version}`, { method: 'DELETE', headers: headers(token, idempotencyKey) }),
  clear: (cart: Cart, token: string, idempotencyKey: string) => call<Cart>(`/api/v1/carts/${cart.id}/items?version=${cart.version}`, { method: 'DELETE', headers: headers(token, idempotencyKey) })
}
