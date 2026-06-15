#!/usr/bin/env bash
# Integration test harness for OrderFlow. Hits the running DevHost (all three
# services in one process on a shared in-memory bus) and asserts the distributed
# saga, JWT auth, RBAC, and cache behaviour.
B=${1:-http://127.0.0.1:5080}
pass=0; fail=0
chk(){ if [ "$1" = "$2" ]; then echo "  PASS: $3 (got $1)"; pass=$((pass+1)); else echo "  FAIL: $3 (expected $2, got $1)"; fail=$((fail+1)); fi; }

# Poll GET /orders/{id} until status matches $2 (or timeout). Echoes final status.
poll_status(){
  local id=$1 want=$2 tok=$3 i status
  for i in $(seq 1 30); do
    status=$(curl -s $B/orders/$id -H "Authorization: Bearer $tok" | grep -o '"status":"[^"]*"' | cut -d'"' -f4)
    [ "$status" = "$want" ] && break
    sleep 0.3
  done
  echo "$status"
}

echo "===== 1. UNAUTHENTICATED ACCESS IS REJECTED (JWT required) ====="
chk "$(curl -s -o /dev/null -w '%{http_code}' $B/orders)" "401" "GET /orders without token -> 401"
chk "$(curl -s -o /dev/null -w '%{http_code}' $B/catalog)" "401" "GET /catalog without token -> 401"
chk "$(curl -s -o /dev/null -w '%{http_code}' -X POST $B/orders -H 'Content-Type: application/json' -d '{}')" "401" "POST /orders without token -> 401"

echo "===== 2. LOGIN / JWT ISSUANCE ====="
alice=$(curl -s -X POST $B/auth/token -H 'Content-Type: application/json' -d '{"username":"alice","password":"password"}')
A_TOK=$(echo "$alice" | grep -o '"accessToken":"[^"]*"' | cut -d'"' -f4)
[ -n "$A_TOK" ] && { echo "  PASS: alice (Customer) received a JWT"; pass=$((pass+1)); } || { echo "  FAIL: alice no token ($alice)"; fail=$((fail+1)); }

bob=$(curl -s -X POST $B/auth/token -H 'Content-Type: application/json' -d '{"username":"bob","password":"password"}')
B_TOK=$(echo "$bob" | grep -o '"accessToken":"[^"]*"' | cut -d'"' -f4)
[ -n "$B_TOK" ] && { echo "  PASS: bob (Customer) received a JWT"; pass=$((pass+1)); } || { echo "  FAIL: bob no token"; fail=$((fail+1)); }

admin=$(curl -s -X POST $B/auth/token -H 'Content-Type: application/json' -d '{"username":"admin","password":"admin"}')
ADM_TOK=$(echo "$admin" | grep -o '"accessToken":"[^"]*"' | cut -d'"' -f4)
[ -n "$ADM_TOK" ] && { echo "  PASS: admin (Admin) received a JWT"; pass=$((pass+1)); } || { echo "  FAIL: admin no token"; fail=$((fail+1)); }

chk "$(curl -s -o /dev/null -w '%{http_code}' -X POST $B/auth/token -H 'Content-Type: application/json' -d '{"username":"alice","password":"WRONG"}')" "401" "wrong password -> 401"
chk "$(curl -s -o /dev/null -w '%{http_code}' $B/orders -H 'Authorization: Bearer not.a.real.token')" "401" "garbage token -> 401"

echo "===== 3. SAGA HAPPY PATH: order -> payment -> inventory -> Confirmed ====="
resp=$(curl -s -X POST $B/orders -H "Authorization: Bearer $A_TOK" -H 'Content-Type: application/json' \
  -d '{"items":[{"sku":"BOOK-001","quantity":2,"unitPrice":39.99},{"sku":"MOUSE-001","quantity":1,"unitPrice":24.99}]}')
OID=$(echo "$resp" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
echo "  created order $OID"
[ -n "$OID" ] && { echo "  PASS: POST /orders accepted (202)"; pass=$((pass+1)); } || { echo "  FAIL: no order id ($resp)"; fail=$((fail+1)); }
st=$(poll_status "$OID" "Confirmed" "$A_TOK")
chk "$st" "Confirmed" "happy-path order reaches Confirmed via saga"

echo "===== 4. SAGA PAYMENT DECLINE: SKU DECLINE -> payment fails -> Cancelled ====="
resp=$(curl -s -X POST $B/orders -H "Authorization: Bearer $A_TOK" -H 'Content-Type: application/json' \
  -d '{"items":[{"sku":"DECLINE","quantity":1,"unitPrice":10.00}]}')
OID2=$(echo "$resp" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
st=$(poll_status "$OID2" "Cancelled" "$A_TOK")
chk "$st" "Cancelled" "declined-payment order reaches Cancelled"
reason=$(curl -s $B/orders/$OID2 -H "Authorization: Bearer $A_TOK" | grep -o '"statusReason":"[^"]*"' | cut -d'"' -f4)
echo "  cancel reason: $reason"

echo "===== 5. SAGA OVER-CREDIT-LIMIT: total > 5000 -> Cancelled ====="
resp=$(curl -s -X POST $B/orders -H "Authorization: Bearer $A_TOK" -H 'Content-Type: application/json' \
  -d '{"items":[{"sku":"LAPTOP-001","quantity":4,"unitPrice":1499.00}]}')
OID3=$(echo "$resp" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
st=$(poll_status "$OID3" "Cancelled" "$A_TOK")
chk "$st" "Cancelled" "over-credit-limit order reaches Cancelled"

echo "===== 6. SAGA OUT-OF-STOCK + REFUND COMPENSATION: RARE-001 qty 2 ====="
# RARE-001 has StockOnHand=1; ordering 2 passes payment then fails reservation,
# which triggers the refund compensation and cancels the order.
resp=$(curl -s -X POST $B/orders -H "Authorization: Bearer $A_TOK" -H 'Content-Type: application/json' \
  -d '{"items":[{"sku":"RARE-001","quantity":2,"unitPrice":199.00}]}')
OID4=$(echo "$resp" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
st=$(poll_status "$OID4" "Cancelled" "$A_TOK")
chk "$st" "Cancelled" "out-of-stock order reaches Cancelled (compensation ran)"
reason=$(curl -s $B/orders/$OID4 -H "Authorization: Bearer $A_TOK" | grep -o '"statusReason":"[^"]*"' | cut -d'"' -f4)
echo "  cancel reason: $reason"

echo "===== 7. REDIS-STYLE CACHE: GET /catalog MISS then HIT ====="
h1=$(curl -s -D - -o /dev/null $B/catalog -H "Authorization: Bearer $A_TOK" | grep -i '^X-Cache:' | tr -d '\r' | awk '{print $2}')
h2=$(curl -s -D - -o /dev/null $B/catalog -H "Authorization: Bearer $A_TOK" | grep -i '^X-Cache:' | tr -d '\r' | awk '{print $2}')
chk "$h1" "MISS" "first /catalog read is a cache MISS"
chk "$h2" "HIT"  "second /catalog read is a cache HIT"

echo "===== 8. RBAC: customer blocked from admin actions, admin allowed ====="
chk "$(curl -s -o /dev/null -w '%{http_code}' $B/admin/orders -H "Authorization: Bearer $A_TOK")" "403" "customer GET /admin/orders -> 403"
chk "$(curl -s -o /dev/null -w '%{http_code}' $B/admin/orders -H "Authorization: Bearer $ADM_TOK")" "200" "admin GET /admin/orders -> 200"
chk "$(curl -s -o /dev/null -w '%{http_code}' -X POST $B/catalog -H "Authorization: Bearer $A_TOK" -H 'Content-Type: application/json' -d '{"sku":"NEW-1","name":"x","price":1,"stockOnHand":1}')" "403" "customer POST /catalog -> 403"
chk "$(curl -s -o /dev/null -w '%{http_code}' -X POST $B/catalog -H "Authorization: Bearer $ADM_TOK" -H 'Content-Type: application/json' -d '{"sku":"NEW-1","name":"Gadget","price":9.99,"stockOnHand":10}')" "200" "admin POST /catalog -> 200"

echo "===== 9. DATA ISOLATION: bob cannot read alice's order ====="
chk "$(curl -s -o /dev/null -w '%{http_code}' $B/orders/$OID -H "Authorization: Bearer $B_TOK")" "403" "bob GET alice's order -> 403"
chk "$(curl -s -o /dev/null -w '%{http_code}' $B/orders/$OID -H "Authorization: Bearer $ADM_TOK")" "200" "admin GET any order -> 200"

echo ""
echo "============================================"
echo "  RESULTS:  PASS=$pass  FAIL=$fail"
echo "============================================"
exit $fail
