# Ecommerce.Api

Lightweight ASP.NET Core Web API for a sample E-commerce website (C#).

## Overview

This repository contains the backend API for a simple e-commerce application built with ASP.NET Core and Entity Framework Core. It provides endpoints for authentication, product management, and order processing.

## Tech stack

- .NET 8 / ASP.NET Core
- Entity Framework Core
- C#

## Prerequisites

- .NET 8 SDK (install from https://dotnet.microsoft.com)
- A SQL Server instance (LocalDB, SQL Server, or Dockerized SQL)

## Quick start

1. Clone the repo:

   git clone https://github.com/thelastmedici/E-commerce-website-with-c-.git
   cd E-commerce-website-with-c-

2. Start a local SQL Server instance using Docker:

   docker compose up -d

3. Restore and run the API:

   cd Ecommerce.Api
   dotnet restore
   dotnet run --project Ecommerce.Api.csproj

4. The API will be available at `https://localhost:5001` or the port shown in the console.

## Database setup

This project uses SQL Server with Entity Framework Core. A Docker Compose file is included to spin up a local SQL Server instance for development.

Connection strings are defined in `Ecommerce.Api/appsettings.json` and `Ecommerce.Api/appsettings.Development.json`.

To create the database schema:

```bash
cd Ecommerce.Api
dotnet ef database update
```

If you change the model, create a new migration with:

```bash
cd Ecommerce.Api
dotnet ef migrations add <MigrationName>
```

## Common commands

- Restore dependencies: `dotnet restore`
- Run the API: `dotnet run --project Ecommerce.Api.csproj`
- Build: `dotnet build`
- Run integration tests: `dotnet test ../Ecommerce.Api.Tests/Ecommerce.Api.Tests.csproj`

If you use EF Core migrations locally, you can add/apply migrations:

- Add migration: `dotnet ef migrations add InitialCreate -p Ecommerce.Api.csproj`
- Update database: `dotnet ef database update -p Ecommerce.Api.csproj`

(Install `dotnet-ef` global tool if needed.)

## API Endpoints (controllers)

- `AuthController` — user registration & login
- `ProductsController` — product listing and details
- `OrdersController` — create and manage orders

Inspect controller sources in the `Controllers/` folder for routes and example request formats.

## Project structure

- `Controllers/` — API controllers
- `Data/` — EF `AppDbContext` and data access
- `DTOs/` — request/response DTOs
- `Models/` — domain models (`Product`, `Order`, `OrderItem`, `Users`)
- `appsettings.json` — configuration

## Contributing

Contributions welcome. Create issues or PRs against `main`.

## License

This project is provided as-is. Add a license file if you intend to open-source it (e.g., MIT).
