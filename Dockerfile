FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Ecommerce.Api/Ecommerce.Api.csproj Ecommerce.Api/
RUN dotnet restore Ecommerce.Api/Ecommerce.Api.csproj

COPY Ecommerce.Api/ Ecommerce.Api/
RUN dotnet publish Ecommerce.Api/Ecommerce.Api.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Ecommerce.Api.dll"]
