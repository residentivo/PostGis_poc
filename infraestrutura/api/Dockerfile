# =====================================================================
# infraestrutura/api/Dockerfile - imagem da api_mapas (.NET 10)
#
# Build (contexto = raiz do repositorio):
#   docker build -f infraestrutura/api/Dockerfile -t api_mapas:local .
#   docker build -f infraestrutura/api/Dockerfile --target resultados \
#       --output type=local,dest=resultados_testes .     (testes + cobertura)
#
# RA-5: a API recusa subir fora do loopback. No container ela ouve em
# 127.0.0.1:5080 e o nginx, que compartilha o mesmo namespace de rede
# (docker-compose.yml), e o unico que publica portas.
# =====================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY api_mapas/ ./
RUN dotnet restore ApiMapas.sln

# Testes unitarios (os de integracao sao opt-in, PG_RUN_INTEGRATION=1, e
# ficam de fora aqui). Falha de teste interrompe o build.
FROM build AS testes
RUN dotnet test ApiMapas.sln --no-restore -c Release \
        --collect:"XPlat Code Coverage" \
        --logger "trx;LogFileName=testes.trx" \
        --results-directory /resultados

# So os resultados, para o Jenkins extrair com --output (gate de cobertura).
FROM scratch AS resultados
COPY --from=testes /resultados /

FROM build AS publicacao
RUN dotnet publish src/ApiMapas.Api/ApiMapas.Api.csproj --no-restore -c Release \
        -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=publicacao /app ./

# ASPNETCORE_HTTP_PORTS vem como 8080 na imagem base e faria o Kestrel ouvir
# em todas as interfaces; a API so aceita ASPNETCORE_URLS em loopback.
ENV ASPNETCORE_HTTP_PORTS= \
    ASPNETCORE_URLS=http://127.0.0.1:5080 \
    DOTNET_EnableDiagnostics=0

# Usuario sem privilegio da imagem base (app, uid 1654).
USER $APP_UID
ENTRYPOINT ["dotnet", "ApiMapas.Api.dll"]
