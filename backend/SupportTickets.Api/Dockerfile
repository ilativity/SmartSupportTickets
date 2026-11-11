# API image. Build context = repository root (the API needs Core, Adapters and the seed dataset):
#   docker build -f backend/SupportTickets.Api/Dockerfile .
# Usually built by docker-compose.yml.

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
# Project files first, so `restore` is cached until a dependency actually changes.
COPY backend/SupportTickets.Core/SupportTickets.Core.csproj backend/SupportTickets.Core/
COPY backend/SupportTickets.Adapters/SupportTickets.Adapters.csproj backend/SupportTickets.Adapters/
COPY backend/SupportTickets.Api/SupportTickets.Api.csproj backend/SupportTickets.Api/
RUN dotnet restore backend/SupportTickets.Api/SupportTickets.Api.csproj
COPY backend/ backend/
RUN dotnet publish backend/SupportTickets.Api/SupportTickets.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app .
COPY dataset.json /app/seed/dataset.json

# Data (tickets.json + uploads) lives on a volume, owned by the image's non-root "app" user.
RUN mkdir -p /data && chown "$APP_UID" /data
VOLUME /data

ENV ASPNETCORE_URLS=http://+:8080 \
    Storage__DataDirectory=/data \
    Storage__SeedFile=/app/seed/dataset.json

USER $APP_UID
EXPOSE 8080

# The runtime image has no curl/wget; bash's /dev/tcp is enough to ask /health.
HEALTHCHECK --interval=10s --timeout=3s --start-period=10s --retries=3 \
  CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health HTTP/1.0\\r\\n\\r\\n' >&3 && grep -q Healthy <&3"]

ENTRYPOINT ["dotnet", "SupportTickets.Api.dll"]
