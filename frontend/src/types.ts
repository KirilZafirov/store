export type CartItem = { productId: string; name: string; unitPrice: number; quantity: number; lineTotal: number }
export type Cart = { id: string; items: CartItem[]; subtotal: number; currency: string | null; version: number; createdAt: string; updatedAt: string }
export type CreatedCart = { cart: Cart; accessToken: string }
export type Product = { id: string; name: string; description: string; price: number; accent: string }
