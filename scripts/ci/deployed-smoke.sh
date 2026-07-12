#!/usr/bin/env bash
set -euo pipefail

api_url="${API_URL:-https://atlas-cart-api-kiril.onrender.com}"
storefront_url="${STOREFRONT_URL:-https://atlas-cart-store.vercel.app}"

json_value() {
  node -e "let body=''; process.stdin.on('data', c => body += c); process.stdin.on('end', () => { const value = JSON.parse(body); console.log($1); });"
}

curl --fail --silent --show-error "$storefront_url" >/dev/null
curl --fail --silent --show-error "$api_url/health/ready" >/dev/null
curl --fail --silent --show-error "$api_url/openapi/v1.json" >/dev/null

created="$(curl --fail --silent --show-error -X POST "$api_url/api/v1/carts")"
cart_id="$(printf '%s' "$created" | json_value 'value.cart.id')"
token="$(printf '%s' "$created" | json_value 'value.accessToken')"

updated="$(
  curl --fail --silent --show-error -X POST "$api_url/api/v1/carts/$cart_id/items" \
    -H 'Content-Type: application/json' \
    -H "X-Cart-Token: $token" \
    -H "Idempotency-Key: deployed-smoke-$(date +%s)-$RANDOM" \
    -d '{"productId":"10000000-0000-0000-0000-000000000001","name":"Orbit headphones","unitPrice":149.00,"currency":"EUR","quantity":1,"version":0}'
)"

subtotal="$(printf '%s' "$updated" | json_value 'value.subtotal')"
quantity="$(printf '%s' "$updated" | json_value 'value.items[0].quantity')"

if [[ "$subtotal" != "149" || "$quantity" != "1" ]]; then
  echo "Unexpected deployed cart result: subtotal=$subtotal quantity=$quantity" >&2
  exit 1
fi

echo "Deployed smoke passed for $api_url and $storefront_url"
