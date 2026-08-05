#!/usr/bin/env bash
set -euo pipefail

PREFIX="ScaleTriggerDemo"
SUBSCRIPTION_ID=""
FORCE=false
NO_WAIT=false

while [ $# -gt 0 ]; do
  case "$1" in
    --prefix) PREFIX="$2"; shift 2 ;;
    --subscription) SUBSCRIPTION_ID="$2"; shift 2 ;;
    --force) FORCE=true; shift ;;
    --no-wait) NO_WAIT=true; shift ;;
    *) echo "Unknown argument: $1" >&2; exit 1 ;;
  esac
done

RESOURCE_GROUPS=(
  "${PREFIX}-Automation"
  "${PREFIX}-Database"
  "${PREFIX}-ServicePlan"
  "${PREFIX}-ScaleSet"
  "${PREFIX}-SingleVM"
  "${PREFIX}-Logs"
)

if [ -n "$SUBSCRIPTION_ID" ]; then
  az account set --subscription "$SUBSCRIPTION_ID"
fi

echo "The following resource groups will be permanently deleted, along with everything inside them:"
for rg in "${RESOURCE_GROUPS[@]}"; do
  if az group show --name "$rg" >/dev/null 2>&1; then
    echo "  - $rg (exists)"
  else
    echo "  - $rg (not found, skipping)"
  fi
done

if [ "$FORCE" != true ]; then
  read -r -p "Are you sure you want to continue? Type exactly 'YES' to confirm: " confirmation
  if [ "$confirmation" != "YES" ]; then
    echo "Aborted - nothing was deleted."
    exit 0
  fi
fi

DELETE_FLAGS="--yes"
if [ "$NO_WAIT" = true ]; then
  DELETE_FLAGS="$DELETE_FLAGS --no-wait"
fi

for rg in "${RESOURCE_GROUPS[@]}"; do
  if az group show --name "$rg" >/dev/null 2>&1; then
    echo "Deleting $rg ..."
    az group delete --name "$rg" $DELETE_FLAGS
  else
    echo "Skipping $rg (not found)."
  fi
done

if [ "$NO_WAIT" = true ]; then
  echo "Deletion started asynchronously for all existing groups. Check progress with:"
  echo "  az group list --query \"[?starts_with(name, '$PREFIX')].{name:name, state:properties.provisioningState}\" -o table"
else
  echo "All existing resource groups have been deleted."
fi
