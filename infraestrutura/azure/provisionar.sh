#!/usr/bin/env bash
# =====================================================================
# infraestrutura/azure/provisionar.sh - cria (uma vez) os recursos da
# Azure para a api_mapas. Precisa do Azure CLI logado (az login).
#
#   IP_ADMIN=203.0.113.10/32 IP_JENKINS=198.51.100.20/32 \
#       bash infraestrutura/azure/provisionar.sh
#
# Cria: grupo de recursos, Azure Container Registry (Basic, sem admin),
# VM Ubuntu 24.04 com Docker (cloud-init.yaml), NSG com SSH so do admin
# e do Jenkins, HTTP/HTTPS abertos, e um service principal com AcrPush
# no registro (usuario/senha para a credencial "acr-credenciais" do
# Jenkins). A senha do service principal e mostrada UMA vez no fim:
# copie direto para o Jenkins e nao guarde em arquivo.
# Idempotente na maior parte (az ... create em recurso existente atualiza).
# =====================================================================
set -Eeuo pipefail

GRUPO=${GRUPO:-rg-postgis-poc}
LOCAL=${LOCAL:-brazilsouth}
ACR_NOME=${ACR_NOME:-acrpostgispoc}          # 5-50 letras/numeros, unico na Azure
VM_NOME=${VM_NOME:-vm-api-mapas}
VM_TAMANHO=${VM_TAMANHO:-Standard_B2s}
VM_USUARIO=azureuser
CHAVE_SSH=${CHAVE_SSH:-$HOME/.ssh/id_ed25519.pub}
IP_ADMIN=${IP_ADMIN:?defina IP_ADMIN (ex.: 203.0.113.10/32) para o SSH}
IP_JENKINS=${IP_JENKINS:?defina IP_JENKINS (ex.: 198.51.100.20/32) para o SSH do Jenkins}
DIR=$(cd "$(dirname "$0")" && pwd)

[[ -f $CHAVE_SSH ]] || { echo "Chave publica $CHAVE_SSH nao encontrada (CHAVE_SSH=...)." >&2; exit 1; }

echo ">> Grupo de recursos $GRUPO ($LOCAL)"
az group create -n "$GRUPO" -l "$LOCAL" -o none

echo ">> Container Registry $ACR_NOME"
az acr create -g "$GRUPO" -n "$ACR_NOME" --sku Basic --admin-enabled false -o none
ACR_ID=$(az acr show -g "$GRUPO" -n "$ACR_NOME" --query id -o tsv)

echo ">> VM $VM_NOME"
az vm create -g "$GRUPO" -n "$VM_NOME" \
    --image Ubuntu2404 \
    --size "$VM_TAMANHO" \
    --admin-username "$VM_USUARIO" \
    --ssh-key-values "$CHAVE_SSH" \
    --public-ip-sku Standard \
    --os-disk-size-gb 64 \
    --custom-data "$DIR/cloud-init.yaml" \
    --nsg-rule NONE \
    -o none

NSG=$(az network nsg list -g "$GRUPO" --query "[?contains(name, '$VM_NOME')].name | [0]" -o tsv)
echo ">> Regras do NSG $NSG"
az network nsg rule create -g "$GRUPO" --nsg-name "$NSG" -n ssh-admin-jenkins \
    --priority 1000 --access Allow --protocol Tcp --direction Inbound \
    --source-address-prefixes "$IP_ADMIN" "$IP_JENKINS" --destination-port-ranges 22 -o none
az network nsg rule create -g "$GRUPO" --nsg-name "$NSG" -n http-https \
    --priority 1010 --access Allow --protocol Tcp --direction Inbound \
    --source-address-prefixes Internet --destination-port-ranges 80 443 -o none

IP_VM=$(az vm show -d -g "$GRUPO" -n "$VM_NOME" --query publicIps -o tsv)

echo ">> Service principal do Jenkins (AcrPush em $ACR_NOME)"
SP_NOME="sp-jenkins-$ACR_NOME"
SP_SENHA=$(az ad sp create-for-rbac -n "$SP_NOME" --role AcrPush --scopes "$ACR_ID" --query password -o tsv)
SP_ID=$(az ad sp list --display-name "$SP_NOME" --query "[0].appId" -o tsv)

cat <<FIM

================ Recursos criados ================
Registro (parametro ACR_NOME do Jenkins): $ACR_NOME  ->  $ACR_NOME.azurecr.io
VM (parametro VM_HOST do Jenkins):        $IP_VM   (usuario $VM_USUARIO)

Credenciais a cadastrar no Jenkins (Manage Jenkins > Credentials):
  acr-credenciais  (Username with password)
      username: $SP_ID
      password: (mostrada abaixo UMA vez)
  azure-vm-ssh     (SSH Username with private key)
      username: $VM_USUARIO
      chave privada correspondente a $CHAVE_SSH (ou outra autorizada na VM)

Senha do service principal:
$SP_SENHA

Proximo passo (aguarde ~3 min o cloud-init terminar):
  scp infraestrutura/servidor/preparar_servidor.sh $VM_USUARIO@$IP_VM:
  ssh $VM_USUARIO@$IP_VM 'cloud-init status --wait && sudo bash preparar_servidor.sh'
==================================================
FIM
