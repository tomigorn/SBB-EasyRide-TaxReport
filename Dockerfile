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

# Generate self-signed certificate for HTTPS
RUN openssl req -x509 -newkey rsa:4096 -keyout /app/localhost.key -out /app/localhost.crt \
    -days 365 -nodes -subj "/CN=localhost" && \
    openssl pkcs12 -export -out /app/localhost.pfx -inkey /app/localhost.key -in /app/localhost.crt -passout pass:

# Expose ports
EXPOSE 8080
EXPOSE 8081

# Set environment to Production by default
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=https://+:8081;http://+:8080
ENV ASPNETCORE_Kestrel__Certificates__Default__Path=/app/localhost.pfx
ENV ASPNETCORE_Kestrel__Certificates__Default__Password=

# Run the application
ENTRYPOINT ["dotnet", "SBB.EasyRide.TaxReport.Web.dll"]
