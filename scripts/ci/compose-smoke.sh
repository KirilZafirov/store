#!/usr/bin/env bash
set -euo pipefail

api_url="${API_URL:-http://localhost:8080}"
web_url="${WEB_URL:-http://localhost:5173}"

wait_for() {
  local url="$1"
  local label="$2"

  for _ in {1..60}; do
    if curl --fail --silent --show-error "$url" >/dev/null; then
      echo "$label is ready"
      return 0
    fi

    sleep 2
  done

  echo "$label did not become ready at $url" >&2
  return 1
}

json_value() {
  node -e "let body=''; process.stdin.on('data', c => body += c); process.stdin.on('end', () => { const value = JSON.parse(body); console.log($1); });"
}

wait_for "$api_url/health/live" "API liveness"
wait_for "$api_url/health/ready" "API readiness"
wait_for "$web_url" "Storefront"

curl --fail --silent --show-error "$api_url/openapi/v1.json" >/dev/null
curl --fail --silent --show-error "$api_url/swagger" >/dev/null
curl --fail --silent --show-error "$api_url/metrics" >/dev/null

created="$(curl --fail --silent --show-error -X POST "$api_url/api/v1/carts")"
cart_id="$(printf '%s' "$created" | json_value 'value.cart.id')"
token="$(printf '%s' "$created" | json_value 'value.accessToken')"

updated="$(
  curl --fail --silent --show-error -X POST "$api_url/api/v1/carts/$cart_id/items" \
    -H 'Content-Type: application/json' \
    -H "X-Cart-Token: $token" \
    -H 'Idempotency-Key: compose-smoke-add-orbit' \
    -d '{"productId":"10000000-0000-0000-0000-000000000001","name":"Orbit headphones","unitPrice":149.00,"currency":"EUR","quantity":1,"version":0}'
)"

subtotal="$(printf '%s' "$updated" | json_value 'value.subtotal')"
quantity="$(printf '%s' "$updated" | json_value 'value.items[0].quantity')"

if [[ "$subtotal" != "149" || "$quantity" != "1" ]]; then
  echo "Unexpected cart smoke result: subtotal=$subtotal quantity=$quantity" >&2
  exit 1
fi

echo "Compose smoke passed"
