# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["src/SBB-EasyRide-TaxReport.sln", "src/"]
COPY ["src/SBB.EasyRide.TaxReport.Web/SBB.EasyRide.TaxReport.Web.csproj", "src/SBB.EasyRide.TaxReport.Web/"]
COPY ["src/SBB.EasyRide.TaxReport.Infrastructure/SBB.EasyRide.TaxReport.Infrastructure.csproj", "src/SBB.EasyRide.TaxReport.Infrastructure/"]
COPY ["src/SBB.EasyRide.TaxReport.Api/SBB.EasyRide.TaxReport.Api.csproj", "src/SBB.EasyRide.TaxReport.Api/"]

# Restore dependencies
RUN dotnet restore "src/SBB-EasyRide-TaxReport.sln"

# Copy all source files
COPY src/ src/

# Build and publish
WORKDIR "/src/src/SBB.EasyRide.TaxReport.Web"
RUN dotnet publish "SBB.EasyRide.TaxReport.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Install OpenSSL for certificate generation
RUN apt-get update && apt-get install -y openssl && rm -rf /var/lib/apt/lists/*

# Copy published files
COPY --from=build /app/publish .

# NOTE: Do not generate or bake private keys into the image. A self-signed certificate
# was previously created at build time; instead we'll generate an untrusted self-signed
# certificate at container startup if none is provided. See `entrypoint.sh`.

# Expose ports
# HTTPS only
EXPOSE 8081

# Set environment to Production by default
ENV ASPNETCORE_ENVIRONMENT=Production
# Listen on HTTPS only inside container
ENV ASPNETCORE_URLS=https://+:8081
ENV ASPNETCORE_Kestrel__Certificates__Default__Path=/app/localhost.pfx

# Copy entrypoint which generates a runtime cert if needed, and start the app
COPY entrypoint.sh /app/entrypoint.sh
RUN chmod +x /app/entrypoint.sh

ENTRYPOINT ["/app/entrypoint.sh"]
