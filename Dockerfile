# Сборка отделена от исполнения: в итоговый образ не попадают ни SDK, ни исходники.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Сначала только файлы проектов: слой с восстановлением пакетов переиспользуется,
# пока не изменились зависимости.
COPY RISL.slnx ./
COPY RISL.Domain/RISL.Domain.csproj RISL.Domain/
COPY RISL.Application/RISL.Application.csproj RISL.Application/
COPY RISL.Infrastructure/RISL.Infrastructure.csproj RISL.Infrastructure/
COPY RISL.Blazor/RISL.Blazor.csproj RISL.Blazor/
COPY RISL.Tests/RISL.Tests.csproj RISL.Tests/
RUN dotnet restore RISL.Blazor/RISL.Blazor.csproj

COPY . .
RUN dotnet publish RISL.Blazor/RISL.Blazor.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# ffmpeg перекодирует загруженные записи, ffprobe проверяет, что файл вообще видео.
# curl нужен только проверке состояния контейнера.
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

# База и медиа лежат на томе: они переживают пересборку образа.
ENV ASPNETCORE_URLS=http://+:8080 \
    Database__Path=/data/risl.db \
    Media__RootPath=/data/media

EXPOSE 8080

ENTRYPOINT ["dotnet", "RISL.Blazor.dll"]
