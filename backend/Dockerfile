FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY backend/ ./backend/
RUN dotnet publish backend/src/FlowOps.Api/FlowOps.Api.csproj -c Release -o /out/api --no-self-contained /p:UseAppHost=false \
    && dotnet publish backend/src/FlowOps.Migrator/FlowOps.Migrator.csproj -c Release -o /out/migrator --no-self-contained /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS migrator
WORKDIR /app
COPY --from=build /out/migrator/ .
USER $APP_UID
ENTRYPOINT ["dotnet", "FlowOps.Migrator.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app
COPY --from=build /out/api/ .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "FlowOps.Api.dll"]
