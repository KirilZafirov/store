import { useEffect, useState } from 'react'
import { ApiError, api } from './api'
import type { Cart, Product } from './types'
import './styles.css'

const products: Product[] = [
  { id: '10000000-0000-0000-0000-000000000001', name: 'Orbit headphones', description: 'Spatial sound · 32-hour battery', price: 149, accent: 'violet' },
  { id: '10000000-0000-0000-0000-000000000002', name: 'Contour keyboard', description: 'Low-profile · Wireless', price: 89, accent: 'amber' },
  { id: '10000000-0000-0000-0000-000000000003', name: 'Arc desk light', description: 'Adaptive warmth · USB-C', price: 64, accent: 'cyan' }
]

const storage = { id: 'atlas.cart.id', token: 'atlas.cart.token' }
const money = (amount: number) => new Intl.NumberFormat('en', { style: 'currency', currency: 'EUR' }).format(amount)

export default function App() {
  const [cart, setCart] = useState<Cart | null>(null)
  const [token, setToken] = useState(localStorage.getItem(storage.token) ?? '')
  const [busy, setBusy] = useState(true)
  const [error, setError] = useState('')

  const start = async () => {
    setBusy(true); setError('')
    try {
      const existingId = localStorage.getItem(storage.id)
      if (existingId && token) setCart(await api.get(existingId, token))
      else {
        const created = await api.create()
        localStorage.setItem(storage.id, created.cart.id); localStorage.setItem(storage.token, created.accessToken)
        setToken(created.accessToken); setCart(created.cart)
      }
    } catch (reason) {
      if (reason instanceof ApiError && [403, 404].includes(reason.status)) {
        localStorage.removeItem(storage.id); localStorage.removeItem(storage.token); setToken('')
      }
      setError(reason instanceof Error ? reason.message : 'Something went wrong.')
    } finally { setBusy(false) }
  }

  useEffect(() => {
    const timer = window.setTimeout(() => void start(), 0)
    return () => window.clearTimeout(timer)
    // The initial restore intentionally runs once; subsequent attempts are explicit retries.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const mutate = async (action: (current: Cart) => Promise<Cart>) => {
    if (!cart) return
    setBusy(true); setError('')
    try { setCart(await action(cart)) }
    catch (reason) {
      if (reason instanceof ApiError && reason.status === 409) {
        setError('Your cart changed elsewhere. We refreshed it; please try the action again.')
        setCart(await api.get(cart.id, token))
      } else setError(reason instanceof Error ? reason.message : 'Something went wrong.')
    } finally { setBusy(false) }
  }

  return <main>
    <header className="topbar">
      <a className="brand" href="#top" aria-label="Atlas home"><span>A</span> ATLAS</a>
      <div className="cart-pill" aria-live="polite"><span>Cart</span><strong>{cart?.items.reduce((sum, item) => sum + item.quantity, 0) ?? 0}</strong></div>
    </header>

    <section className="hero" id="top">
      <p className="eyebrow">DESIGNED FOR FOCUS</p>
      <h1>Tools that feel<br/><em>effortless.</em></h1>
      <p className="intro">A focused storefront demonstrating a resilient, versioned cart workflow.</p>
    </section>

    {error && <div className="error" role="alert"><span>{error}</span><button onClick={() => void start()}>Retry</button></div>}

    <section className="catalog" aria-labelledby="catalog-title">
      <div className="section-heading"><p>01</p><h2 id="catalog-title">Essentials</h2></div>
      <div className="product-grid">
        {products.map((product, index) => <article className={`product ${product.accent}`} key={product.id}>
          <div className="visual"><span className="shape">{['◖', '⌨', '◒'][index]}</span><span className="index">0{index + 1}</span></div>
          <div className="product-copy"><div><h3>{product.name}</h3><p>{product.description}</p></div><strong>{money(product.price)}</strong></div>
          <button disabled={busy || !cart} onClick={() => void mutate(c => api.add(c, token, product))}>Add to cart <span>+</span></button>
        </article>)}
      </div>
    </section>

    <section className="cart-panel" aria-labelledby="cart-title">
      <div className="section-heading light"><p>02</p><h2 id="cart-title">Your cart</h2></div>
      {busy && !cart ? <p className="cart-state">Preparing your cart…</p> : cart?.items.length === 0 ? <div className="empty"><p>Nothing here yet.</p><span>Choose an essential above to begin.</span></div> : <>
        <div className="lines">{cart?.items.map(item => <div className="line" key={item.productId}>
          <div><h3>{item.name}</h3><button className="remove" disabled={busy} onClick={() => void mutate(c => api.remove(c, token, item.productId))}>Remove</button></div>
          <div className="quantity" aria-label={`Quantity for ${item.name}`}><button aria-label="Decrease quantity" disabled={busy || item.quantity === 1} onClick={() => void mutate(c => api.quantity(c, token, item.productId, item.quantity - 1))}>−</button><span>{item.quantity}</span><button aria-label="Increase quantity" disabled={busy} onClick={() => void mutate(c => api.quantity(c, token, item.productId, item.quantity + 1))}>+</button></div>
          <strong>{money(item.lineTotal)}</strong>
        </div>)}</div>
        <div className="summary"><button className="clear" disabled={busy} onClick={() => void mutate(c => api.clear(c, token))}>Clear cart</button><div><span>Subtotal</span><strong>{money(cart?.subtotal ?? 0)}</strong><small>Taxes and shipping calculated at checkout</small></div></div>
      </>}
    </section>
    <footer><span>ATLAS / CART SERVICE DEMO</span><span>Strong cart consistency · Retry-safe writes</span></footer>
  </main>
}
