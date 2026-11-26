#!/usr/bin/env bash
# Started by livingserver-alloy.service (issue #649). The settings come from monitoring.env via EnvironmentFile=;
# nothing is printed.
set -euo pipefail
cd "$(dirname "$0")"
HERE="$(pwd)"  # absolute paths, so detect can tell this Alloy from any other on the PC
# The stack page may give the bare Loki URL; add the push path when it is missing.
case "${GC_LOKI_URL:-}" in */loki/api/v1/push) ;; *) export GC_LOKI_URL="${GC_LOKI_URL%/}/loki/api/v1/push" ;; esac
export LS_ALLOY_ADDR="${LS_ALLOY_ADDR:-127.0.0.1:12347}"
exec "$HERE/bin/alloy" run "$HERE/config" --server.http.listen-addr="$LS_ALLOY_ADDR" --storage.path="$HERE/data" --disable-reporting
