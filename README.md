# bank-reconciliation-api

Work in progress. A .NET 10 Web API that fetches bank transactions, matches incoming payments to open invoices, reports unmatched items and notifies an ERP via signed webhooks.

## Quick start

```bash
cp .env.example .env   # set the database password, JWT key and demo user passwords
docker compose up --build
```

- Swagger UI: http://localhost:8080/swagger
- Health: http://localhost:8080/health (liveness), http://localhost:8080/health/ready (database)
- FakeBank: http://localhost:8081

Compose applies migrations and seeds fictional demo data (2 accounts, 4 customers, 8 invoices) on startup.

## Security

- **Sign in:** `POST /api/v1/auth/login` with `admin@example.com` or `accountant@example.com` and the passwords from `.env`,
  then click **Authorize** in Swagger UI and paste the `accessToken`.
- **JWT** (HMAC-SHA256, 60 minutes). The signing key comes from `.env`; the API refuses to start without a key of at least 32 characters.
- **Roles:** every endpoint requires a signed-in user. Accountants read everything, manage customers and invoices and do
  manual matching. Admins can additionally manage bank accounts and users, delete customers and read the audit log.
- **Passwords** are hashed with PBKDF2 (ASP.NET Core Identity hasher). Login is limited to 5 attempts per minute per IP
  and doesn't reveal whether an email exists.
- **IBAN masking:** non-admin users see `TR71******************0011`.
- **Audit log:** logins, manual matches, reversals, automatic matches and changes to customers, invoices, accounts and users are
  recorded in the same database transaction as the change (`GET /api/v1/audit-log`).

## Manual matching

- `POST /api/v1/transactions/{id}/matches`: match a transaction to one or more invoices by hand.
- `DELETE /api/v1/matches/{id}` with a reason: undo a match. The transaction is then excluded from automatic matching,
  so the engine doesn't re-apply a match a person rejected.
- `POST /api/v1/transactions/{id}/ignore` and `/restore`: take a transaction (e.g. a bank fee) out of reconciliation and back.
- `GET /api/v1/transactions?status=Unmatched&incoming=true`: what still needs attention.

Manual changes and automatic reconciliation share one lock, so they never update the same invoice at the same time.

## How transactions arrive

- **FakeBank** simulates a bank statement API (`GET /api/v1/accounts/{iban}/transactions?from=&page=`).
  The main account has 11 scripted movements covering matching edge cases; every account also gets random
  movements derived from (IBAN, time slot), so data is stable across restarts and new movements appear over time.
  It fails 20% of requests and adds up to 500 ms latency on purpose (`Chaos__*` settings).
- A **Quartz.NET** job imports new movements of every active account every 5 minutes (`Jobs__BankSync__*`).
  `POST /api/v1/transactions/import` runs an import immediately.
- The HTTP client uses the **Polly** standard resilience pipeline: retries with exponential backoff and jitter,
  per-attempt timeout and a circuit breaker.
- A **Redis** lock (`SET NX PX` + compare-and-delete release) makes sure only one import runs at a time across all
  API instances. A unique index on (account, bank transaction id) guarantees a movement is never stored twice.

## How payments are matched

After each import the job runs the matching engine over unmatched incoming transactions.
It scores evidence and applies proposals scoring **80 or more** (`Reconciliation__AutoMatchThreshold`);
the rest wait for manual matching.

| Signal | Points |
|---|---|
| Invoice number in description (any formatting: `inv2026-0008`, `INV/2026/0008`) | 70 |
| Only the digits of the invoice number (`Invoice 2026 0003`) | 60 |
| Payer identified by counterparty IBAN or by tax number (VKN) in description | 20 |
| Payer identified by company name only | 10 |
| Payer belongs to a different customer than the invoice | -40 |
| Amount equals open amount / is a partial payment | 10 / 5 |
| No reference, but exactly one open invoice of the payer has this amount | 50 |

- One payment naming several invoices is split across them, oldest due date first.
- An overpayment pays the invoice in full; the remainder stays unmatched on the transaction.
- Two open invoices with the same amount and no reference are never guessed.
- `GET /api/v1/transactions/{id}/suggestions` shows the engine's proposal, score and reasons without storing anything.
- `POST /api/v1/reconciliation/run` runs matching immediately; `GET /api/v1/matches` lists matches with their reasons.

## Build and test

```bash
dotnet build
dotnet test
```

## Running the API outside Docker

Start only the database (`docker compose up -d mssql`), then store the connection string as a user secret:

```bash
dotnet user-secrets --project src/Reconciliation.Api set "ConnectionStrings:Default" "Server=localhost,1433;Database=Reconciliation;User Id=sa;Password=<your password>;TrustServerCertificate=True"
dotnet tool restore
dotnet ef database update -p src/Reconciliation.Infrastructure -s src/Reconciliation.Api
dotnet run --project src/Reconciliation.Api
```

To insert the demo data here too, set `Database__Seed=true` before `dotnet ef database update`.

Add a migration after changing the model:

```bash
dotnet ef migrations add <Name> -p src/Reconciliation.Infrastructure -s src/Reconciliation.Api -o Persistence/Migrations
```

## License

MIT
