#!/usr/bin/env bash
# =====================================================================
# infraestrutura/jenkins/publicar_acr.sh - estagio "Publicar no ACR" do
# Jenkinsfile. Confere cada pre-requisito na ordem e, se algo falhar,
# mostra a causa provavel e como resolver (em vez de so o erro do docker).
#
# Variaveis (do Jenkinsfile): REGISTRO, API_TAG, POSTGIS_TAG e, da
# credencial acr-credenciais, ACR_USUARIO e ACR_SENHA.
# A senha so passa pela entrada padrao do docker login; nunca e impressa.
# =====================================================================
set -Euo pipefail

ACR_NOME=${REGISTRO%%.*}

falhar() {
    local problema=$1
    shift
    echo
    echo "=================================================================="
    echo "ERRO ao publicar no ACR: $problema"
    echo "Como resolver:"
    for dica in "$@"; do
        echo "  - $dica"
    done
    echo "=================================================================="
    exit 1
}

echo ">> 1/6 Credencial acr-credenciais"
[[ -n ${ACR_USUARIO:-} && -n ${ACR_SENHA:-} ]] || falhar "usuario ou senha vazios na credencial acr-credenciais." \
    "Manage Jenkins > Credentials: a credencial deve ser do tipo 'Username with password'." \
    "username = appId do service principal; password = senha mostrada pelo azure/provisionar.sh."

echo ">> 2/6 Imagens locais"
for imagem in "$REGISTRO/api_mapas:$API_TAG" "$REGISTRO/postgis_mapas:$POSTGIS_TAG"; do
    docker image inspect "$imagem" >/dev/null 2>&1 || falhar "a imagem $imagem nao existe no agente." \
        "Confira o estagio 'Imagens' deste build (ele precisa ter terminado sem erro)." \
        "O agente que rodou 'Imagens' deve ser o mesmo deste estagio (agent any com varios agentes pode trocar)."
done

echo ">> 3/6 DNS de $REGISTRO"
if command -v getent >/dev/null && ! getent hosts "$REGISTRO" >/dev/null; then
    falhar "o nome $REGISTRO nao resolve no agente." \
        "Confira o parametro ACR_NOME (so o nome, sem .azurecr.io). Registros existentes: az acr list -o table" \
        "Se o ACR usa private endpoint, o agente precisa estar na VNet ou ter o DNS privado (privatelink.azurecr.io)."
fi

echo ">> 4/6 Acesso HTTPS a https://$REGISTRO/v2/"
if command -v curl >/dev/null; then
    codigo=$(curl -s -o /dev/null -w '%{http_code}' --max-time 15 "https://$REGISTRO/v2/" || true)
    case $codigo in
        401|200) echo "   ok (HTTP $codigo)" ;;
        000) falhar "sem resposta de https://$REGISTRO (timeout, TLS ou proxy)." \
                 "Teste no agente: curl -v https://$REGISTRO/v2/" \
                 "Firewall/proxy da rede do Jenkins precisa liberar saida 443 para *.azurecr.io e *.blob.core.windows.net." \
                 "Erro de certificado (x509): proxy corporativo inspecionando TLS; instale a CA dele no agente." ;;
        403) falhar "o ACR recusou o IP do agente (HTTP 403)." \
                 "O ACR esta com acesso publico restrito. Libere o IP de saida do Jenkins:" \
                 "az acr network-rule add -n $ACR_NOME --ip-address <ip-do-jenkins>   (exige SKU Premium)" \
                 "Ou: az acr update -n $ACR_NOME --public-network-enabled true" ;;
        *) echo "   resposta inesperada (HTTP $codigo); seguindo para o login." ;;
    esac
else
    echo "   curl ausente no agente; teste pulado."
fi

echo ">> 5/6 docker login"
if ! saida=$(echo "$ACR_SENHA" | docker login "$REGISTRO" -u "$ACR_USUARIO" --password-stdin 2>&1); then
    echo "$saida"
    case $saida in
        *"authentication required"*)
            falhar "o ACR nao aceitou o service principal neste registro ($ACR_NOME)." \
                "Causa mais comum: o parametro ACR_NOME aponta para outro registro, diferente do criado pelo azure/provisionar.sh. Registros: az acr list -o table" \
                "Ou o service principal nao tem papel neste registro. Veja os papeis: az role assignment list --assignee <appId> --all -o table" \
                "Para dar o papel: az role assignment create --assignee <appId> --role AcrPush --scope \$(az acr show -n $ACR_NOME --query id -o tsv)   (vale em alguns minutos)" \
                "Confira tambem se o username da credencial e o appId (GUID), nao o nome do service principal." ;;
        *unauthorized*|*401*|*"incorrect username or password"*|*"Invalid client"*)
            falhar "usuario/senha recusados pelo ACR." \
                "Confira se o username e o appId (GUID) do service principal: az ad sp list --display-name <nome-do-sp> --query '[].appId' -o tsv" \
                "A senha do service principal expira (padrao 1 ano). Gere outra e atualize a credencial no Jenkins:" \
                "az ad sp credential reset --id <appId> --query password -o tsv" ;;
        *"Cannot connect to the Docker daemon"*|*"permission denied"*docker.sock*)
            falhar "o usuario do Jenkins nao acessa o Docker do agente." \
                "No agente: sudo usermod -aG docker jenkins && sudo systemctl restart jenkins (ou do servico do agente)." ;;
        *)
            falhar "docker login falhou (mensagem acima)." \
                "Teste manual no agente: docker login $REGISTRO -u <appId>" ;;
    esac
fi
echo "   ok"

empurrar() {
    local imagem=$1 saida
    if ! saida=$(docker push "$imagem" 2>&1); then
        echo "$saida"
        case $saida in
            *denied*|*insufficient_scope*|*unauthorized*)
                falhar "login ok, mas o service principal nao pode enviar imagens para $ACR_NOME." \
                    "Ele precisa do papel AcrPush no registro (a atribuicao pode levar alguns minutos para valer):" \
                    "az role assignment create --assignee <appId> --role AcrPush --scope \$(az acr show -n $ACR_NOME --query id -o tsv)" ;;
            *quota*|*"storage limit"*|*QuotaExceeded*)
                falhar "o ACR atingiu o limite de armazenamento do SKU." \
                    "Apague tags antigas: az acr repository show-tags -n $ACR_NOME --repository api_mapas --orderby time_asc -o tsv" \
                    "az acr repository delete -n $ACR_NOME --image api_mapas:<tag> --yes   (ou suba o SKU)" ;;
            *x509*|*certificate*)
                falhar "erro de certificado TLS no push." \
                    "Proxy corporativo inspecionando TLS: instale a CA dele no agente e reinicie o Docker." ;;
            *)
                falhar "docker push de $imagem falhou (mensagem acima)." \
                    "Repita no agente com detalhes: docker --debug push $imagem" ;;
        esac
    fi
    echo "   $imagem enviada."
}

echo ">> 6/6 docker push"
empurrar "$REGISTRO/api_mapas:$API_TAG"
if docker manifest inspect "$REGISTRO/postgis_mapas:$POSTGIS_TAG" >/dev/null 2>&1; then
    echo "   postgis_mapas:$POSTGIS_TAG ja existe no ACR; push pulado."
else
    empurrar "$REGISTRO/postgis_mapas:$POSTGIS_TAG"
fi
