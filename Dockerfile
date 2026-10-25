FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["src/server/Megaphonk.Server.csproj", "src/server/"]
COPY ["src/common/Megaphonk.Common.csproj", "src/common/"]
RUN dotnet restore "src/server/Megaphonk.Server.csproj"

COPY . .
WORKDIR "/src/src/server"
RUN dotnet publish "Megaphonk.Server.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
ENV ASPNETCORE_URLS=http://+:5432
EXPOSE 5432
EXPOSE 5678/udp
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Megaphonk.Server.dll"]