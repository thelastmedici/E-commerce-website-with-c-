# Ecommerce API

An ASP.NET Core Web API for a sample e-commerce application. The API provides JWT authentication, product management, stock-aware order processing, and SQL Server persistence.

## Stack

- .NET 8 and ASP.NET Core
- Entity Framework Core 8
- SQL Server 2022
- JWT bearer authentication
- BCrypt password hashing
- Swagger/OpenAPI in development
- Health and readiness probes
- IP-based request rate limiting
- Structured JSON request logs with correlation IDs
- xUnit integration tests with SQLite in-memory storage

## Prerequisites

- .NET 8 SDK
- Docker Desktop or Docker Engine with Compose
- `dotnet-ef` global tool for migrations

Install the EF tool if needed:

```bash
dotnet tool install --global dotnet-ef
```

## Configuration

Secrets are intentionally excluded from tracked configuration files. Configure the API through environment variables, user secrets, or a deployment secret manager.

Required settings:

```bash
export Jwt__Key='replace-with-a-random-secret-at-least-32-characters-long'
export ConnectionStrings__DefaultConnection='Server=localhost,1433;Database=EcommerceDb;User Id=sa;Password=<your-password>;TrustServerCertificate=True;'
```

At least one browser origin must be allowlisted for CORS:

```bash
export Cors__AllowedOrigins__0='http://localhost:3000'
export Cors__AllowedOrigins__1='http://localhost:5173'
```

The application fails during startup when the JWT key, database connection string, or CORS allowlist is missing. Do not commit real secrets to this repository.

To provision the first administrator, configure these one-time bootstrap settings through environment variables or user secrets:

```bash
export AdminBootstrap__Email='admin@example.com'
export AdminBootstrap__Password='use-a-unique-password-at-least-12-characters'
```

When both settings are present, startup creates the administrator if the email does not exist. It is idempotent for an existing administrator and refuses to promote an existing regular user. Remove the bootstrap password from the environment after the account is created; the account and its password hash remain in the database.

## Run Locally

From the repository root, set the SQL Server password and start the database:

```bash
export MSSQL_SA_PASSWORD='<your-password>'
docker compose up -d
```

Use the same password in `ConnectionStrings__DefaultConnection`. If this is a clean database, apply every migration before starting the API:

```bash
cd Ecommerce.Api
dotnet restore
dotnet ef database update
dotnet run
```

The API uses the URL printed by ASP.NET Core. With the default launch settings, Swagger is available at:

- `https://localhost:7071`
- `http://localhost:5065`

Swagger is enabled only in the Development environment.

## Database Migrations

Apply existing migrations:

```bash
cd Ecommerce.Api
dotnet ef database update
```

Create a migration after changing the EF model:

```bash
dotnet ef migrations add <MigrationName>
```

The current model includes product stock and a SQL Server `rowversion` concurrency token.

Health endpoints are intentionally public for load balancers and orchestrators:

- `GET /health/live` confirms the process is serving requests.
- `GET /health/ready` checks the SQL Server dependency and returns `503` when the API is not ready.

Non-health requests are rate limited per client IP. The defaults are 100 requests per 60 seconds and can be overridden with `RateLimiting__PermitLimit` and `RateLimiting__WindowSeconds`. Every response includes an `X-Correlation-ID` header; valid request IDs are preserved and invalid or missing IDs are replaced with a generated UUID.

## API Endpoints

All product and order endpoints require a valid bearer token unless stated otherwise.

### Authentication

| Method | Route | Description |
| --- | --- | --- |
| `POST` | `/api/auth/register` | Register a user |
| `POST` | `/api/auth/login` | Authenticate and receive a JWT |

Register a user:

```bash
curl -X POST http://localhost:5065/api/auth/register \
  -H 'Content-Type: application/json' \
  -d '{"email":"customer@example.com","password":"Password123!"}'
```

Log in and copy the returned `token` value:

```bash
curl -X POST http://localhost:5065/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"customer@example.com","password":"Password123!"}'
```

### Products

| Method | Route | Access |
| --- | --- | --- |
| `GET` | `/api/products` | Authenticated users |
| `GET` | `/api/products/{id}` | Authenticated users |
| `POST` | `/api/products` | Admins only |
| `PUT` | `/api/products/{id}` | Admins only |
| `DELETE` | `/api/products/{id}` | Admins only |

Products referenced by existing orders cannot be deleted.

Product listings support optional search, price, stock, and pagination parameters:

```text
/api/products?search=phone&minPrice=10&maxPrice=500&inStock=true&page=1&pageSize=20
```

`pageSize` is limited to 100 items per request.

Use the returned bearer token to query the catalog:

```bash
curl 'http://localhost:5065/api/products?search=phone&inStock=true&page=1&pageSize=20' \
  -H 'Authorization: Bearer <token>'
```

Admin product creation request:

```bash
curl -X POST http://localhost:5065/api/products \
  -H 'Authorization: Bearer <admin-token>' \
  -H 'Content-Type: application/json' \
  -d '{"name":"Wireless phone","price":299.99,"stock":25}'
```

### Orders

| Method | Route | Access |
| --- | --- | --- |
| `GET` | `/api/orders` | Own orders; admins see all |
| `GET` | `/api/orders/{id}` | Owner or admin |
| `POST` | `/api/orders` | Authenticated users |
| `PATCH` | `/api/orders/{id}/status` | Admins only |
| `POST` | `/api/orders/{id}/cancel` | Owner or admin |
| `POST` | `/api/orders/{id}/refund` | Admins only |

Order ownership is taken from the authenticated JWT identity. Clients do not submit a `UserId`. Order creation validates product existence, checks stock, decrements stock transactionally, and captures the product price at purchase time.

Orders move through `Pending`, `Confirmed`, `Shipped`, and `Delivered`. Owners can cancel pending or confirmed orders, which restores stock. Admins can record refunds for shipped or delivered orders. The refund endpoint records the order state; connecting it to an external payment provider still requires provider-specific credentials and webhook handling.

Example order request:

```json
{
  "items": [
    {
      "productId": 1,
      "quantity": 2
    }
  ]
}
```

Cancel an eligible order:

```bash
curl -X POST http://localhost:5065/api/orders/1/cancel \
  -H 'Authorization: Bearer <token>'
```

Advance an order as an admin:

```bash
curl -X PATCH http://localhost:5065/api/orders/1/status \
  -H 'Authorization: Bearer <admin-token>' \
  -H 'Content-Type: application/json' \
  -d '{"status":"Confirmed"}'
```

## Tests

The integration suite starts the real API pipeline with an isolated SQLite in-memory database. It covers:

- Registration and login
- JWT-protected endpoints
- Admin-only product management
- Product validation
- Stock limits
- Order ownership
- Concurrent orders and overselling protection

Run all tests from the repository root:

```bash
dotnet test Ecommerce.Api.Tests/Ecommerce.Api.Tests.csproj
```

The CI pipeline runs these integration tests with SQLite and a separate SQL Server job that applies every EF migration to a clean database.

## Production Deployment

The repository includes a multi-stage, non-root [Dockerfile](Dockerfile). The `app` Compose profile runs the API behind Caddy, which terminates HTTPS and obtains certificates automatically for the configured domain.

For a local or VM deployment:

```bash
cp .env.example .env
# Set real values in .env, including DOMAIN, JWT_KEY, and MSSQL_SA_PASSWORD.
docker compose --profile app up -d --build
```

Point DNS for `DOMAIN` to the host before starting Caddy. Apply migrations before starting the API profile:

```bash
docker compose up -d sqlserver
(cd Ecommerce.Api && dotnet ef database update)
docker compose --profile app up -d --build
```

The API is kept on the internal Compose network and only Caddy publishes ports 80 and 443. Compose opts into trusting forwarded headers because Caddy is the only public ingress; do not enable `ForwardedHeaders:TrustAll` when the API is directly internet-facing. For managed production platforms, inject `Jwt__Key`, `ConnectionStrings__DefaultConnection`, CORS origins, and bootstrap credentials through the platform secret manager instead of using a committed file or Docker image layer.

SQL Server data is stored in the named `sqlserver-data` volume. The backup command writes inside the container, copies the verified backup to `./backups`, and removes the temporary container copy:

```bash
MSSQL_SA_PASSWORD='<password>' DATABASE_NAME=EcommerceDb ./scripts/backup-sqlserver.sh
```

Backups are written to `./backups` with a seven-day local retention default. Copy backups to encrypted, durable off-host storage and regularly test restoring one before treating the deployment as production-ready.

Container logs are JSON formatted and include the request method, path, status code, duration, and correlation ID. Forward stdout to the hosting platform's log and alerting system; no third-party monitoring credential is required by the application.

## Project Structure

```text
Ecommerce.Api/
  Controllers/     HTTP endpoints
  Data/            EF Core DbContext
  DTOs/            Request and response contracts
  Migrations/      EF Core database migrations
  Models/          Domain entities
  Observability/   Health and request-correlation middleware
  Program.cs       Application startup and middleware
  Security/        Admin bootstrap logic
Ecommerce.Api.Tests/
  ApiTests.cs      Integration test scenarios
  TestApplicationFactory.cs
Dockerfile         Production API image
Caddyfile          HTTPS reverse proxy configuration
scripts/           Operational database backup script
```

## Security Notes

- JWT signing keys and database passwords must be supplied externally.
- CORS uses an explicit origin allowlist and fails closed when none is configured.
- Product mutations require the `Admin` role.
- Users cannot access another user’s orders.
- Passwords are stored as BCrypt hashes, never plaintext.

## License

This project is provided as-is. Add a license file before distributing it as open source.
