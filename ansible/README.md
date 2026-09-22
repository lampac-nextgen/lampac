# Lampac NextGen

Idempotent install for Debian/Ubuntu (amd64, arm64). Same result as `install.sh`: OS packages, Google Chrome, ASP.NET Core 10, user `lampac`, GitHub release under `/opt/lampac`, systemd unit `lampac`.

Copy `inventory/hosts.yml.example` to your own inventory and set `ansible_host`.

```bash
ansible-playbook -i ansible/inventory/hosts.yml ansible/site.yml
ansible-playbook -i ansible/inventory/hosts.yml ansible/site.yml -e lampac_version=v1.2.3
ansible-playbook -i ansible/inventory/hosts.yml ansible/site.yml -e lampac_state=absent -e lampac_confirm_remove=true
```

A second run does not re-download when `version.txt` already matches. `-e lampac_force=true` syncs that version again. `-e lampac_prerelease=true` takes the newest pre-release (do not combine it with `lampac_version`). `ansible-playbook --check` previews; it does not replace a real host run.
