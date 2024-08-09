FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["FlexPkg/FlexPkg.csproj", "FlexPkg/"]
COPY ["FlexPkg.Common/FlexPkg.Common.csproj", "FlexPkg.Common/"]
COPY ["FlexPkg.Steam/FlexPkg.Steam.csproj", "FlexPkg.Steam/"]
COPY ["FlexPkg.Database/FlexPkg.Database.csproj", "FlexPkg.Database/"]
COPY ["FlexPkg.SqliteMigrations/FlexPkg.SqliteMigrations.csproj", "FlexPkg.SqliteMigrations/"]
COPY ["FlexPkg.MySqlMigrations/FlexPkg.MySqlMigrations.csproj", "FlexPkg.MySqlMigrations/"]
COPY ["FlexPkg.PostgreSqlMigrations/FlexPkg.PostgreSqlMigrations.csproj", "FlexPkg.PostgreSqlMigrations/"]
RUN dotnet restore "FlexPkg/FlexPkg.csproj"
COPY . .
WORKDIR "/src/FlexPkg"
RUN dotnet build "FlexPkg.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "FlexPkg.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FlexPkg.dll"]
