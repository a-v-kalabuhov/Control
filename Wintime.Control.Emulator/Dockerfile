# Stage 1: Build Vue frontend
FROM node:20-alpine AS frontend-build
WORKDIR /web
COPY Wintime.Control.Emulator/Web/package*.json ./
RUN npm ci
COPY Wintime.Control.Emulator/Web/ ./
# vite.config.js: outDir = resolve(__dirname, '../wwwroot') → /wwwroot
RUN npm run build

# Stage 2: Build .NET app
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["Wintime.Control.Emulator/Wintime.Control.Emulator.csproj", "Wintime.Control.Emulator/"]
RUN dotnet restore "Wintime.Control.Emulator/Wintime.Control.Emulator.csproj" /p:SkipFrontendBuild=true
COPY . .
COPY --from=frontend-build /wwwroot Wintime.Control.Emulator/wwwroot/
RUN dotnet publish "Wintime.Control.Emulator/Wintime.Control.Emulator.csproj" \
    -c Release -o /app/publish \
    /p:UseAppHost=false \
    /p:SkipFrontendBuild=true

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "Wintime.Control.Emulator.dll"]
