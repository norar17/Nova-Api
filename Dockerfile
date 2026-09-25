# --- Build stage ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first for better Docker layer caching (restore only re-runs if these change)
COPY ECommerce.sln .
COPY src/Shared/Shared.csproj src/Shared/
COPY src/Domain/Domain.csproj src/Domain/
COPY src/Application/Application.csproj src/Application/
COPY src/Infrastructure/Infrastructure.csproj src/Infrastructure/
COPY src/API/API.csproj src/API/

RUN dotnet restore src/API/API.csproj

# Now copy everything else and build
COPY . .
RUN dotnet publish src/API/API.csproj -c Release -o /app/publish --no-restore

# --- Runtime stage ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_hostBuilder__reloadConfigOnChange=false

COPY --from=build /app/publish .

# Render injects $PORT at container startup (not build time), and Program.cs reads it directly
# to configure Kestrel's listen URL — see the PORT handling block near the top of Program.cs.
ENTRYPOINT ["dotnet", "API.dll"]
