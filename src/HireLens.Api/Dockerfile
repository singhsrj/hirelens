# HireLens API (ASP.NET Core, .NET 10). Build from the repo root:
#   docker build -t hirelens-api src/HireLens.Api
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY HireLens.Api.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
# The base image runs as the non-root "app" user and listens on 8080.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "HireLens.Api.dll"]
