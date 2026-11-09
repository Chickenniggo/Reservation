FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source
COPY NuGet.Config ./
COPY src/Reservations.Api/Reservations.Api.csproj src/Reservations.Api/packages.lock.json src/Reservations.Api/
RUN dotnet restore src/Reservations.Api/Reservations.Api.csproj --locked-mode
COPY src/ src/
RUN dotnet publish src/Reservations.Api/Reservations.Api.csproj -c Release --no-restore -o /publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /publish/ ./
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Reservations.Api.dll"]