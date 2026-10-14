# =============================================================================
# Stage 1: Build
# =============================================================================
FROM mcr.microsoft.com/dotnet/sdk:6.0 AS builder

WORKDIR /src

# Copy project file first for dependency caching
COPY EcommerceWebApi.csproj ./
RUN dotnet restore EcommerceWebApi.csproj

# Copy remaining source code
COPY . .

# Build and publish in Release configuration
RUN dotnet publish EcommerceWebApi.csproj -c Release -o /app/publish --no-restore

# =============================================================================
# Stage 2: Runtime
# =============================================================================
FROM mcr.microsoft.com/dotnet/runtime:6.0 AS runtime

WORKDIR /app

# Create non-root user for security
RUN addgroup --system --gid 1001 appgroup \
    && adduser --system --uid 1001 --ingroup appgroup --no-create-home appuser

# Copy published output from builder stage
COPY --from=builder /app/publish .

# Create Logs directory and set ownership
RUN mkdir -p /app/Logs && chown -R appuser:appgroup /app

# Switch to non-root user
USER appuser

# Expose application port
EXPOSE 3000

# Environment variables
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_HTTP_PORT=3000
ENV DOTNET_RUNNING_IN_CONTAINER=true
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

ENTRYPOINT ["dotnet", "EcommerceWebApi.dll"]
