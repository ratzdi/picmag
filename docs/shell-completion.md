# Bash Completion

If installed via Debian package, completion is placed at `/etc/bash_completion.d/picmag`.

Load for current shell:

```bash
source /etc/bash_completion
```

Test:

```bash
picmag --qua<TAB>
picmag --quality-review /path --action <TAB>
```

Enable in `~/.bashrc`:

```bash
if [ -f /etc/bash_completion ]; then
  . /etc/bash_completion
fi
```
