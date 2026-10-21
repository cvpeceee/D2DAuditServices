FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/api/ ./
RUN dotnet restore src/D2DAudit.Api/D2DAudit.Api.csproj
RUN dotnet publish src/D2DAudit.Api/D2DAudit.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "D2DAudit.Api.dll"]
