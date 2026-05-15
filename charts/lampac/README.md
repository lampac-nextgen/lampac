# Lampac Helm Chart

Deploys [Lampac Next Generation](https://github.com/lampac-nextgen/lampac) on Kubernetes (image `ghcr.io/lampac-nextgen/lampac`).

## Quick install (local-k8s)

```bash
helm upgrade --install lampac ./charts/lampac \
  -n demo --create-namespace \
  -f ./helm/lampac-values.yaml
```

## Config from the node filesystem (`hostPath`)

Lampac reads `/lampac/init.conf` and optionally `/lampac/passwd` (same layout as [upstream Docker docs](https://github.com/lampac-nextgen/lampac)).

To use a directory on your machine (on single-node clusters the node path is often the same as macOS/OrbStack paths):

```yaml
config:
  fromHost:
    enabled: true
    hostPath: /path/on/node/containing/init.conf
  mountPasswd: true   # also needs passwd next to init.conf on the node
```

## Values overview

| Key | Purpose |
|-----|---------|
| `service.port` / `service.targetPort` | Cluster Service port and container listen port (match `listen.port` in `init.conf` when overriding). |
| `config.fromHost` | Mount `init.conf` (+ optional `passwd`) from a node directory via `subPath`. |
| `persistence.cache` / `persistence.database` | PVCs for `/lampac/cache` and `/lampac/database`. |
| `readinessProbe` / `startupProbe` | Defaults use **TCP** on the HTTP port so Ready/Helm `--wait` do not depend on `/version` during a long startup; change to `httpGet` if you prefer. |
| `httpRoute` | Gateway API HTTPRoute (see `helm/lampac-values.yaml` in this repo). |
