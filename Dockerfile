# syntax=docker/dockerfile:1

# Three images from one file:
#
#   docker build -t game-of-life .                 the app (the `runtime` stage, ~230 MB: ASP.NET runtime + the published site)
#   docker build --target test .                   builds and runs every test, Playwright's Chromium included; fails if they do
#   docker run -p 8080:8080 -v game-of-life:/home/app/.local/share/dilyanrusev/game-of-life game-of-life
#
# CI (.github/workflows/ci.yml) builds the `test-deps` stage instead and runs `dotnet test` in a
# container from it, so the tests execute on every run even when every layer is a cache hit.
#
# On Azure App Service (Web App for Containers) set WEBSITES_PORT=8080; ASPNETCORE_FORWARDEDHEADERS_ENABLED
# is baked in below because TLS terminates at the platform's proxy. The saved universe and the
# data-protection key ring live under /home, which App Service persists across restarts. The image
# runs as the non-root `app` user; if the platform's /home mount turns out not to be writable for
# it, the log says "Could not create the state directory" at start — then either set
# GameOfLife__StateDirectory to a writable path or switch the last stage to `USER root`.
#
# The limits for a small shared host are in appsettings.Production.json and can be overridden with
# environment variables (GameOfLife__MaxGenerationsPerSecond and friends); see README.md.

ARG DOTNET_VERSION=10.0

# ---------------------------------------------------------------------------------------------
# SDK plus Node: the csproj builds the client (TypeScript check, esbuild) as part of the build.
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS base
RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl gnupg \
 && curl -fsSL https://deb.nodesource.com/setup_22.x | bash - \
 && apt-get install -y --no-install-recommends nodejs \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /src

# ---------------------------------------------------------------------------------------------
# Playwright's Chromium and its system libraries, for the tests. Placed before anything that
# depends on the project files, so neither a source nor a package change downloads the browser
# again. The version must match Microsoft.Playwright in tests/GameOfLife.Web.Tests; the test
# fixture's own install call then finds the browser cached. The runtime image is unaffected.
FROM base AS tooling
ARG PLAYWRIGHT_VERSION=1.52.0
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
RUN npx --yes playwright@${PLAYWRIGHT_VERSION} install --with-deps chromium \
 && rm -rf /var/lib/apt/lists/* /root/.npm/_npx

# ---------------------------------------------------------------------------------------------
# Everything that depends only on the project files, so source changes do not redo it:
# NuGet packages, the TypeScript client generator (a dotnet tool), npm packages.
FROM tooling AS restore
COPY GameOfLife.slnx ./
COPY .config/ .config/
COPY src/GameOfLife.Core/GameOfLife.Core.csproj src/GameOfLife.Core/
COPY src/GameOfLife.Web/GameOfLife.Web.csproj src/GameOfLife.Web/
COPY tests/GameOfLife.Core.Tests/GameOfLife.Core.Tests.csproj tests/GameOfLife.Core.Tests/
COPY tests/GameOfLife.Web.Tests/GameOfLife.Web.Tests.csproj tests/GameOfLife.Web.Tests/
COPY benchmarks/GameOfLife.Benchmarks/GameOfLife.Benchmarks.csproj benchmarks/GameOfLife.Benchmarks/
RUN dotnet restore GameOfLife.slnx && dotnet tool restore
COPY src/GameOfLife.Web/package.json src/GameOfLife.Web/package-lock.json src/GameOfLife.Web/
RUN cd src/GameOfLife.Web && npm ci

# ---------------------------------------------------------------------------------------------
# The site, published. The client build runs as it does on a developer machine (generated hub
# proxy, type check, esbuild --minify), and the static web assets step fingerprints and
# pre-compresses the bundles.
FROM restore AS build
COPY . .
RUN dotnet publish src/GameOfLife.Web -c Release -o /app --no-restore

# ---------------------------------------------------------------------------------------------
# The tests. `test-deps` is everything they need, built; `test` runs them, as the last step, so
# `docker build --target test` fails when they do.
FROM restore AS test-deps
COPY . .

FROM test-deps AS test
RUN dotnet test GameOfLife.slnx -c Release --no-restore

# ---------------------------------------------------------------------------------------------
# What gets deployed: the runtime, the published site and its patterns, nothing else.
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app
COPY --from=build /app .
# Behind a TLS-terminating proxy (App Service): trust X-Forwarded-For / X-Forwarded-Proto.
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
# The state directory, owned by the app user, so a volume mounted here inherits the ownership.
RUN mkdir -p /home/app/.local/share/dilyanrusev/game-of-life && chown -R app:app /home/app
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "GameOfLife.Web.dll"]
