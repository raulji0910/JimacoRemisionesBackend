FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Jimaco.Remisiones.slnx .
COPY Jimaco.Remisiones.Modelo/Jimaco.Remisiones.Modelo.csproj Jimaco.Remisiones.Modelo/
COPY Jimaco.Remisiones.Negocio/Jimaco.Remisiones.Negocio.csproj Jimaco.Remisiones.Negocio/
COPY Jimaco.Remisiones.Api/Jimaco.Remisiones.Api.csproj Jimaco.Remisiones.Api/
COPY Jimaco.Remisiones.TestUnitarios/Jimaco.Remisiones.TestUnitarios.csproj Jimaco.Remisiones.TestUnitarios/
RUN dotnet restore Jimaco.Remisiones.Api/Jimaco.Remisiones.Api.csproj

COPY Jimaco.Remisiones.Modelo/ Jimaco.Remisiones.Modelo/
COPY Jimaco.Remisiones.Negocio/ Jimaco.Remisiones.Negocio/
COPY Jimaco.Remisiones.Api/ Jimaco.Remisiones.Api/

RUN dotnet publish Jimaco.Remisiones.Api/Jimaco.Remisiones.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["dotnet", "Jimaco.Remisiones.Api.dll"]
