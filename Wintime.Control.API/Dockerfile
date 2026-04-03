# =====================================================
# ЭТАП 1: Сборка приложения (.NET 8 SDK)
# =====================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Копируем файлы проектов для восстановления зависимостей
COPY ["Wintime.Control.API/Wintime.Control.API.csproj", "Wintime.Control.API/"]
COPY ["Wintime.Control.Core/Wintime.Control.Core.csproj", "Wintime.Control.Core/"]
COPY ["Wintime.Control.Infrastructure/Wintime.Control.Infrastructure.csproj", "Wintime.Control.Infrastructure/"]
COPY ["Wintime.Control.Shared/Wintime.Control.Shared.csproj", "Wintime.Control.Shared/"]

# Восстанавливаем зависимости
RUN dotnet restore "Wintime.Control.API/Wintime.Control.API.csproj"

# Копируем исходный код
COPY . .

# Собираем приложение в режиме Release
WORKDIR "/src/Wintime.Control.API"
RUN dotnet build "Wintime.Control.API.csproj" -c Release -o /app/build

# Публикуем приложение
RUN dotnet publish "Wintime.Control.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

# =====================================================
# ЭТАП 2: Рантайм (минимальный образ)
# =====================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Используем встроенного пользователя 'app' (UID 1000)
# Это стандартный non-root пользователь в образах .NET 8
USER app

# Копируем опубликованное приложение с правильными правами
COPY --from=build --chown=app:app /app/publish .

# Открываем порты
EXPOSE 80
EXPOSE 443

# Переменные окружения
ENV ASPNETCORE_URLS=http://+:80
ENV ASPNETCORE_ENVIRONMENT=Production

# Точка входа
ENTRYPOINT ["dotnet", "Wintime.Control.API.dll"]