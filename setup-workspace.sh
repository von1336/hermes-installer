#!/usr/bin/env bash
# No sudo. Installs Node via nvm if needed, then hermes-workspace.
set -eu

echo "==> Hermes workspace setup (WSL, no sudo)"

# Drop Windows PATH pollution (e.g. /mnt/c/Program Files/nodejs)
export PATH="/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:$HOME/.nvm/versions/node/$(ls "$HOME/.nvm/versions/node" 2>/dev/null | tail -1)/bin:$HOME/.local/bin"

need_node=0
if ! command -v node >/dev/null 2>&1; then
  need_node=1
else
  major=$(node -v | sed 's/v//;s/\..*//')
  if [ "$major" -lt 20 ]; then
    need_node=1
  fi
fi

if [ "$need_node" = 1 ]; then
  echo "==> Installing nvm + Node 22 (user-space, no sudo)..."
  export NVM_DIR="$HOME/.nvm"
  if [ ! -s "$NVM_DIR/nvm.sh" ]; then
    curl -fsSL https://raw.githubusercontent.com/nvm-sh/nvm/v0.40.1/install.sh | bash
  fi
  # shellcheck disable=SC1090
  . "$NVM_DIR/nvm.sh"
  nvm install 22
  nvm alias default 22
else
  echo "==> node already present: $(node -v)"
  if [ -s "$HOME/.nvm/nvm.sh" ]; then
    # shellcheck disable=SC1090
    . "$HOME/.nvm/nvm.sh"
  fi
fi

echo "node=$(command -v node) $(node -v)"

if ! command -v pnpm >/dev/null 2>&1; then
  echo "==> Installing pnpm via corepack/npm..."
  if command -v corepack >/dev/null 2>&1; then
    corepack enable || true
    corepack prepare pnpm@latest --activate || npm install -g pnpm
  else
    npm install -g pnpm
  fi
fi
echo "pnpm=$(command -v pnpm) $(pnpm -v)"

if [ ! -d "$HOME/hermes-workspace/.git" ]; then
  echo "==> Cloning hermes-workspace..."
  git clone https://github.com/outsourc-e/hermes-workspace.git "$HOME/hermes-workspace"
else
  echo "==> Updating hermes-workspace..."
  cd "$HOME/hermes-workspace"
  git pull --ff-only || echo "git pull skipped"
fi

cd "$HOME/hermes-workspace"
echo "==> pnpm install..."
pnpm install
echo "==> WSL workspace setup OK"
