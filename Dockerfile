FROM node:22-bookworm-slim AS assets
WORKDIR /build
COPY package.json package-lock.json ./
RUN --mount=type=secret,id=build_ca \
    if [ -f /run/secrets/build_ca ]; then export NODE_EXTRA_CA_CERTS=/run/secrets/build_ca; fi; \
    npm ci --ignore-scripts
COPY scripts/build-assets.mjs scripts/build-assets.mjs
RUN npm run assets

FROM mcr.microsoft.com/dotnet/sdk:10.0.402 AS build
WORKDIR /build
COPY . .
COPY --from=assets /build/src/DigitalSorter.Web/wwwroot/vendor src/DigitalSorter.Web/wwwroot/vendor
RUN --mount=type=cache,target=/root/.nuget/packages --mount=type=secret,id=build_ca \
    if [ -f /run/secrets/build_ca ]; then export SSL_CERT_FILE=/run/secrets/build_ca; fi; \
    dotnet publish src/DigitalSorter.Auth/DigitalSorter.Auth.csproj -c Release -o /publish -p:UseAppHost=false -p:RestoreLockedMode=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13
WORKDIR /app
COPY --from=build /publish .
RUN chmod -R a+rX /app && mkdir -p /app/data && chown -R "$APP_UID" /app/data
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080 Broker__DataDirectory=/app/data
EXPOSE 8080
ENTRYPOINT ["dotnet", "DigitalSorter.Auth.dll"]
