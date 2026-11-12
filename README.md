# bank-reconciliation-api

Work in progress. A .NET 10 Web API that fetches bank transactions, matches incoming payments to open invoices, reports unmatched items and notifies an ERP via signed webhooks.

## Quick start

```bash
cp .env.example .env   # set MSSQL_SA_PASSWORD
docker compose up --build
```

- Swagger UI: http://localhost:8080/swagger
- Health: http://localhost:8080/health (liveness), http://localhost:8080/health/ready (database)
- FakeBank: http://localhost:8081

Compose applies migrations and seeds fictional demo data (2 accounts, 4 customers, 8 invoices) on startup.

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
