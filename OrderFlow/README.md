# OrderFlow — Microservices-Based Order Processing System

A distributed **.NET 8** order-processing backend built around an event-driven
**saga**. Three services — **Order**, **Payment**, and **Inventory** —
coordinate an asynchronous purchase workflow over a message bus, with
**Redis** caching, **JWT authentication + role-based access control (RBAC)**,
and **SQL Server** persistence. It runs end-to-end on machine with **zero
external dependencies**, and swaps in **Azure Service Bus**, **Redis**, and
**EF Core / SQL Server** for production via a single build flag.

**Stack:** .NET 8 · ASP.NET Core REST APIs · SQL Server (EF Core) · Redis ·
Azure Service Bus · Docker · JWT / RBAC

---

## Why it's built this way

Every cross-service concern is coded against an interface in
`OrderFlow.Contracts`:

| Concern        | Interface       | Dev / test provider          | Production provider (`OrderFlow.Infrastructure`) |
| -------------- | --------------- | ---------------------------- | ------------------------------------------------ |
| Messaging      | `IEventBus`     | in-process channel bus       | Azure Service Bus (topic + per-service subs)     |
| Caching        | `ICacheService` | in-memory distributed cache  | Redis (StackExchange.Redis)                      |
| Order storage  | `IOrderRepository`     | concurrent dictionary | EF Core + SQL Server                             |
| Payment storage| `IPaymentRepository`   | concurrent dictionary | EF Core + SQL Server                             |
| Inventory storage | `IInventoryRepository` | concurrent dictionary | EF Core + SQL Server (serializable + rowversion) |



---

## The saga (choreography + compensation)

Placing an order kicks off an asynchronous, event-driven workflow. There is no
central coordinator; each service reacts to events and emits its own. A failure
at any step triggers **compensation** (a refund) and cancels the order.

```
POST /orders
   │  (Order saved as PaymentProcessing)
   ▼
OrderCreated ──────────────► [Payment] charge
                                  │
                 ┌────────────────┴───────────────┐
        PaymentCompleted                     PaymentFailed
                 │                                 │
                 ▼                                 ▼
        [Inventory] reserve                 Order → Cancelled
                 │
      ┌──────────┴───────────┐
InventoryReserved      InventoryOutOfStock
      │                      │
      ▼                      ▼
Order → Confirmed     [Payment] refund (compensation)
                             │
                       PaymentRefunded
                             ▼
                      Order → Cancelled
```

Deterministic hooks make every branch testable: SKU `DECLINE` or an order total
over the 5000 credit limit forces a payment failure; SKU `RARE-001` is seeded
with stock 1 so ordering 2 forces an out-of-stock + refund.

---

## Security: JWT + RBAC

- `POST /auth/token` validates credentials and issues an **HS256 JWT** carrying
  the user id, name, and role.
- A custom authentication handler (scheme `Bearer`) validates the signature,
  issuer, audience, and expiry on every request.
- Two authorization policies enforce **RBAC**: `Customers` (Customer or Admin)
  and `AdminOnly` (Admin).
- **Data isolation:** a customer can only read their own orders; cross-customer
  reads return `403`. Admins can read any order.

Demo users (seeded):

| Username | Password   | Role     |
| -------- | ---------- | -------- |
| `alice`  | `password` | Customer |
| `bob`    | `password` | Customer |
| `admin`  | `admin`    | Admin    |

---

## Project layout

```
OrderFlow.sln
src/
  OrderFlow.Contracts/        Models, integration events, interfaces, DTOs (no dependencies)
  OrderFlow.Platform/         Shared infra: in-memory bus, cache, JWT/RBAC, AuthController
  OrderFlow.OrderService/     Orders API + saga state handlers
  OrderFlow.PaymentService/   Payment charge / refund handlers
  OrderFlow.InventoryService/ Catalog API + stock reservation handlers
  OrderFlow.Infrastructure/   Production adapters: Redis, Azure Service Bus, EF Core (EnableCloud only)
  OrderFlow.DevHost/          Runs all three services in ONE process on a shared bus (for local run + tests)
tests/
  OrderFlow.IntegrationTests/ xUnit tests (JWT, reservation atomicity, credentials)
  harness/run-tests.sh        Black-box HTTP harness asserting the full saga + auth + cache + RBAC
docker/
  Dockerfile.*                One per service (built with EnableCloud=true)
  docker-compose.yml          SQL Server + Redis + Azure Service Bus emulator + 3 services
  servicebus-emulator-config.json
```

---

## Run it locally (no dependencies)

Requires the .NET 8 SDK.

```bash
dotnet run --project src/OrderFlow.DevHost
# now listening on http://localhost:5080
```

The DevHost mounts all three services in a single process sharing one in-memory
bus, so the full saga runs without Docker, Redis, or Azure. Try it:

```bash
# get a token
TOKEN=$(curl -s -X POST http://localhost:5080/auth/token \
  -H 'Content-Type: application/json' \
  -d '{"username":"alice","password":"password"}' | grep -o '"accessToken":"[^"]*"' | cut -d'"' -f4)

# place an order (happy path) -> poll until Confirmed
curl -s -X POST http://localhost:5080/orders \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"items":[{"sku":"BOOK-001","quantity":2,"unitPrice":39.99}]}'
```

### Run the test harness

```bash
# in one terminal
dotnet run --project src/OrderFlow.DevHost
# in another
bash tests/harness/run-tests.sh
```

### Run the unit tests

```bash
dotnet test
```

---

## Run the production stack (Docker)

Builds each service with `-p:EnableCloud=true` and wires Redis, Azure Service
Bus (emulator), and SQL Server:

```bash
docker compose -f docker/docker-compose.yml up --build
```

Services are exposed on `5001` (orders), `5002` (payments), `5003` (inventory).
The Azure Service Bus emulator requires accepting its EULA (set in compose) and
uses `docker/servicebus-emulator-config.json` to declare the topic and
per-service subscriptions.

### How the provider swap works

Each service's `Program.cs` registers the in-memory providers by default, then,
inside `#if CLOUD`, calls `AddOrderFlowCloud(configuration)`, which reads the
`Providers` config section and replaces `ICacheService` with Redis, `IEventBus`
with Azure Service Bus, and the repositories with EF Core / SQL Server:

```jsonc
"Providers": { "Bus": "ServiceBus", "Cache": "Redis", "Storage": "SqlServer" }
```

The `CLOUD` symbol and the conditional reference to `OrderFlow.Infrastructure`
are switched on by a single MSBuild property:

```bash
dotnet build -p:EnableCloud=true
```

For SQL Server, generate the schema with EF Core migrations:

```bash
dotnet ef migrations add Initial --project src/OrderFlow.Infrastructure
dotnet ef database update --project src/OrderFlow.Infrastructure
```

---

## API reference

| Method | Route               | Auth        | Description                                  |
| ------ | ------------------- | ----------- | -------------------------------------------- |
| POST   | `/auth/token`       | none        | Exchange credentials for a JWT               |
| POST   | `/orders`           | Customer    | Place an order (starts the saga); `202`      |
| GET    | `/orders/{id}`      | Customer*   | Get one order (owner or admin only)          |
| GET    | `/orders`           | Customer    | List the caller's orders                     |
| GET    | `/admin/orders`     | Admin       | List every order                             |
| GET    | `/catalog`          | Customer    | List products (cached; `X-Cache: HIT\|MISS`) |
| POST   | `/catalog`          | Admin       | Create / update a product (invalidates cache)|
| GET    | `/health`           | none        | Liveness                                     |

\* owner-or-admin enforced in the handler.

---

## Test results

The black-box harness runs against the DevHost and asserts the full distributed
workflow. Latest run:

```
1. UNAUTHENTICATED ACCESS IS REJECTED (JWT required)
   PASS: GET /orders without token -> 401
   PASS: GET /catalog without token -> 401
   PASS: POST /orders without token -> 401
2. LOGIN / JWT ISSUANCE
   PASS: alice (Customer) received a JWT
   PASS: bob (Customer) received a JWT
   PASS: admin (Admin) received a JWT
   PASS: wrong password -> 401
   PASS: garbage token -> 401
3. SAGA HAPPY PATH: order -> payment -> inventory -> Confirmed
   PASS: POST /orders accepted (202)
   PASS: happy-path order reaches Confirmed via saga
4. SAGA PAYMENT DECLINE: SKU DECLINE -> payment fails -> Cancelled
   PASS: declined-payment order reaches Cancelled   (reason: Payment failed: card_declined)
5. SAGA OVER-CREDIT-LIMIT: total > 5000 -> Cancelled
   PASS: over-credit-limit order reaches Cancelled
6. SAGA OUT-OF-STOCK + REFUND COMPENSATION: RARE-001 qty 2
   PASS: out-of-stock order reaches Cancelled (compensation ran)   (reason: Out of stock: RARE-001)
7. REDIS-STYLE CACHE: GET /catalog MISS then HIT
   PASS: first /catalog read is a cache MISS
   PASS: second /catalog read is a cache HIT
8. RBAC: customer blocked from admin actions, admin allowed
   PASS: customer GET /admin/orders -> 403
   PASS: admin GET /admin/orders -> 200
   PASS: customer POST /catalog -> 403
   PASS: admin POST /catalog -> 200
9. DATA ISOLATION: bob cannot read alice's order
   PASS: bob GET alice's order -> 403
   PASS: admin GET any order -> 200

RESULTS:  PASS=21  FAIL=0
```

---

## Notes & next steps

- The in-memory bus mirrors the at-least-once, ordered delivery of a single
  Azure Service Bus subscription, which is why the saga behaves identically on
  both transports. The cloud processor abandons messages on handler failure so
  Service Bus retries and ultimately dead-letters them.
- A natural extension is an **idempotency / inbox** table keyed by `EventId`
  (already on every event) so redelivered messages are processed once.
- The JWT here is hand-rolled HS256 for a zero-dependency demo; in production
  prefer `Microsoft.AspNetCore.Authentication.JwtBearer` against an identity
  provider (Entra ID / IdentityServer).
