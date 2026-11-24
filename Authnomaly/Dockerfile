FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS tools
RUN dotnet tool install --tool-path /tools dotnet-counters \
    && dotnet tool install --tool-path /tools dotnet-gcdump
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Authnomaly/Authnomaly.csproj", "Authnomaly/"]
RUN dotnet restore "Authnomaly/Authnomaly.csproj"
COPY . .
WORKDIR "/src/Authnomaly"
RUN dotnet build "./Authnomaly.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Authnomaly.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY --from=tools /tools /tools
ENTRYPOINT ["dotnet", "Authnomaly.dll"]
