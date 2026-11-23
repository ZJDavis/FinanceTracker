FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY FinanceTracker.Api/FinanceTracker.Api.csproj FinanceTracker.Api/
COPY FinanceTracker.Application/FinanceTracker.Application.csproj FinanceTracker.Application/
COPY FinanceTracker.Core/FinanceTracker.Core.csproj FinanceTracker.Core/
COPY FinanceTracker.Data.Postgres/FinanceTracker.Data.Postgres.csproj FinanceTracker.Data.Postgres/
RUN dotnet restore FinanceTracker.Api/FinanceTracker.Api.csproj

COPY FinanceTracker.Api/ FinanceTracker.Api/
COPY FinanceTracker.Application/ FinanceTracker.Application/
COPY FinanceTracker.Core/ FinanceTracker.Core/
COPY FinanceTracker.Data.Postgres/ FinanceTracker.Data.Postgres/
RUN dotnet publish FinanceTracker.Api/FinanceTracker.Api.csproj --configuration Release --no-restore --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "FinanceTracker.Api.dll"]
