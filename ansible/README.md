# Lampac NextGen

Idempotent install for Debian/Ubuntu (amd64, arm64). Same result as `install.sh`: OS packages, Google Chrome, ASP.NET Core 10, user `lampac`, GitHub release under `/opt/lampac`, systemd unit `lampac`.

Copy `inventory/hosts.yml.example` to your own inventory and set `ansible_host`.

```bash
ansible-playbook -i ansible/inventory/hosts.yml ansible/site.yml
ansible-playbook -i ansible/inventory/hosts.yml ansible/site.yml -e lampac_version=v1.2.3
ansible-playbook -i ansible/inventory/hosts.yml ansible/site.yml -e lampac_state=absent -e lampac_confirm_remove=true
```

A second run does not re-download when `version.txt` already matches. `-e lampac_force=true` syncs that version again. `-e lampac_prerelease=true` takes the newest pre-release (do not combine it with `lampac_version`). `ansible-playbook --check` previews; it does not replace a real host run.

`lampac_init_conf` is written to `init.conf` on each run. Its default matches `config/example.init.conf`, and `listen.port` follows `lampac_port`. Set `lampac_init_conf: {}` to leave the host file unchanged. To write `init.yaml` instead, set `lampac_init_conf: {}` and `lampac_init_yaml` to the mapping. Do not set both. `passwd`, `users.json`, and `excludes.conf` are written only when their variable is non-empty.

```yaml
lampac_passwd: "set-this"
lampac_users:
  - id: "user@example.com"
    group: 1
lampac_excludes:
  - "my_custom_folder/"
```
