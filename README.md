# bank-reconciliation-api

Work in progress. A .NET 10 Web API that fetches bank transactions, matches incoming payments to open invoices, reports unmatched items and notifies an ERP via signed webhooks.

## Quick start

```bash
cp .env.example .env   # set MSSQL_SA_PASSWORD
docker compose up --build
```

- Swagger UI: http://localhost:8080/swagger
- Health: http://localhost:8080/health
- FakeBank: http://localhost:8081

## Build and test

```bash
dotnet build
dotnet test
```

## License

MIT
