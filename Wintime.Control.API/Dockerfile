# Stage 1: Build .NET app (без фронтенда)
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["Wintime.Control.Emulator/Wintime.Control.Emulator.csproj", "Wintime.Control.Emulator/"]
RUN dotnet restore "Wintime.Control.Emulator/Wintime.Control.Emulator.csproj" /p:SkipFrontendBuild=true

COPY . .
# Важно: не копируем ничего из frontend-build, только код .NET
RUN dotnet publish "Wintime.Control.Emulator/Wintime.Control.Emulator.csproj" \
    -c Release -o /app/publish \
    /p:UseAppHost=false \
    /p:SkipFrontendBuild=true

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "Wintime.Control.Emulator.dll"]