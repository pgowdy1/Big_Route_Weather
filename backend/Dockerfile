# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY RouteWeather.API/*.csproj RouteWeather.API/
COPY RouteWeather.Core/*.csproj RouteWeather.Core/
COPY RouteWeather.Data/*.csproj RouteWeather.Data/
RUN dotnet restore RouteWeather.API/RouteWeather.API.csproj

COPY RouteWeather.API/ RouteWeather.API/
COPY RouteWeather.Core/ RouteWeather.Core/
COPY RouteWeather.Data/ RouteWeather.Data/
RUN dotnet publish RouteWeather.API/RouteWeather.API.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "RouteWeather.API.dll"]
