# ══════════════════════════════════════════════════════════════════════════════
#  Lampac NextGen — Makefile
#  Targets: dotnet build/publish/clean/format
#           Docker build/push/cache + Compose up/down/logs
#           Dev environment setup + Helm lint/template/dry-run
# ══════════════════════════════════════════════════════════════════════════════

# ── Dotnet configuration ───────────────────────────────────────────────────────
OUTPUT     ?= publish
CONFIG     ?= Release
RUNTIME_ID ?=

# ── Docker / Compose configuration ────────────────────────────────────────────
COMPOSE_FILE     ?= docker-compose.yaml
COMPOSE_DEV_FILE ?= docker-compose.dev.yaml

IMAGE_NAME    ?= ghcr.io/lampac-nextgen/lampac
TAG           ?= latest
ALL_PLATFORMS  = linux/amd64,linux/arm64
BUILDER_NAME   = lampac-builder
PUSH          ?= false
NO_CACHE      ?= false
EXPORT_TAR    ?=
PLATFORM      ?=

# ── Helm configuration ────────────────────────────────────────────────────────
HELM_RELEASE    ?= lampac
HELM_CHART       = charts/lampac
HELM_NAMESPACE  ?= default
HELM_VALUES     ?=

# ── Computed helpers ───────────────────────────────────────────────────────────
FULL_IMAGE     = $(IMAGE_NAME):$(TAG)
DOTNET_ARGS    = Core/Core.csproj -c $(CONFIG) --self-contained false -o $(OUTPUT)
ifdef RUNTIME_ID
  DOTNET_ARGS += -r $(RUNTIME_ID)
endif

NO_CACHE_FLAG =
ifeq ($(NO_CACHE),true)
  NO_CACHE_FLAG = --no-cache
endif

# ── Colors ─────────────────────────────────────────────────────────────────────
BLUE   = \033[0;34m
GREEN  = \033[0;32m
YELLOW = \033[1;33m
RED    = \033[0;31m
NC     = \033[0m

# ── Default target ─────────────────────────────────────────────────────────────
.DEFAULT_GOAL := help
.PHONY: all
all: build ## Build the application (default target)

.PHONY: test
test: check ## Run all checks (alias for check)
.PHONY: help
help: _help-header _help-dotnet _help-docker _help-compose _help-helm _help-other _help-vars ## Show this help

.PHONY: _help-header
_help-header:
	@echo ""
	@echo "$(BLUE)Lampac NextGen$(NC)"

.PHONY: _help-dotnet
_help-dotnet:
	@echo ""
	@echo "$(BLUE)Dotnet targets:$(NC)"
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) \
		| grep -vE '^(docker|dev|up|down|logs|ps|helm|version)' \
		| awk 'BEGIN {FS = ":.*?## "}; {printf "  $(GREEN)%-26s$(NC) %s\n", $$1, $$2}'

.PHONY: _help-docker
_help-docker:
	@echo ""
	@echo "$(BLUE)Docker build targets:$(NC)"
	@grep -E '^docker[-a-zA-Z0-9_]*:.*?## .*$$' $(MAKEFILE_LIST) \
		| awk 'BEGIN {FS = ":.*?## "}; {printf "  $(GREEN)%-26s$(NC) %s\n", $$1, $$2}'

.PHONY: _help-compose
_help-compose:
	@echo ""
	@echo "$(BLUE)Compose targets:$(NC)"
	@grep -E '^(up|down|logs|ps|dev-up|dev-down|dev-logs|dev-ps|dev-setup):.*?## .*$$' $(MAKEFILE_LIST) \
		| awk 'BEGIN {FS = ":.*?## "}; {printf "  $(GREEN)%-26s$(NC) %s\n", $$1, $$2}'

.PHONY: _help-helm
_help-helm:
	@echo ""
	@echo "$(BLUE)Helm targets:$(NC)"
	@grep -E '^helm[-a-zA-Z0-9_]*:.*?## .*$$' $(MAKEFILE_LIST) \
		| awk 'BEGIN {FS = ":.*?## "}; {printf "  $(GREEN)%-26s$(NC) %s\n", $$1, $$2}'

.PHONY: _help-other
_help-other:
	@echo ""
	@echo "$(BLUE)Other targets:$(NC)"
	@grep -E '^(version|check):.*?## .*$$' $(MAKEFILE_LIST) \
		| awk 'BEGIN {FS = ":.*?## "}; {printf "  $(GREEN)%-26s$(NC) %s\n", $$1, $$2}'

.PHONY: _help-vars
_help-vars: _help-vars-dotnet _help-vars-compose _help-vars-helm

.PHONY: _help-vars-dotnet
_help-vars-dotnet:
	@echo ""
	@echo "$(BLUE)Variables (override with VAR=value):$(NC)"
	@echo "  $(YELLOW)OUTPUT$(NC)=$(OUTPUT)        publish output directory"
	@echo "  $(YELLOW)CONFIG$(NC)=$(CONFIG)       build configuration (Debug|Release)"
	@echo "  $(YELLOW)RUNTIME_ID$(NC)=            dotnet runtime identifier (e.g. linux-x64)"

.PHONY: _help-vars-docker
_help-vars-docker:
	@echo "  $(YELLOW)IMAGE_NAME$(NC)=$(IMAGE_NAME)"
	@echo "  $(YELLOW)TAG$(NC)=$(TAG)         image tag"
	@echo "  $(YELLOW)PUSH$(NC)=$(PUSH)        push image after build (true|false)"
	@echo "  $(YELLOW)NO_CACHE$(NC)=$(NO_CACHE)    disable Docker build cache (true|false)"
	@echo "  $(YELLOW)EXPORT_TAR$(NC)=          export single-platform image to tar path"

.PHONY: _help-vars-compose
_help-vars-compose: _help-vars-docker
	@echo "  $(YELLOW)PLATFORM$(NC)=            override target platform (e.g. linux/amd64)"
	@echo ""
	@echo "$(BLUE)Compose variables:$(NC)"
	@echo "  $(YELLOW)COMPOSE_FILE$(NC)=$(COMPOSE_FILE)"
	@echo "  $(YELLOW)COMPOSE_DEV_FILE$(NC)=$(COMPOSE_DEV_FILE)"

.PHONY: _help-vars-helm
_help-vars-helm:
	@echo ""
	@echo "$(BLUE)Helm variables:$(NC)"
	@echo "  $(YELLOW)HELM_RELEASE$(NC)=$(HELM_RELEASE)"
	@echo "  $(YELLOW)HELM_NAMESPACE$(NC)=$(HELM_NAMESPACE)"
	@echo "  $(YELLOW)HELM_VALUES$(NC)=           path to extra -f values file"
	@echo ""

# ══════════════════════════════════════════════════════════════════════════════
#  Dotnet
# ══════════════════════════════════════════════════════════════════════════════

.PHONY: publish
publish: ## Publish the application (OUTPUT, CONFIG, RUNTIME_ID)
	@echo "$(BLUE)[•]$(NC) Publishing → $(OUTPUT)  [$(CONFIG)]"
	dotnet publish $(DOTNET_ARGS)
	@echo "$(GREEN)[✓]$(NC) Published to $(OUTPUT)"

.PHONY: build
build: ## Build (no publish) — alias for dotnet build
	@echo "$(BLUE)[•]$(NC) Building [$(CONFIG)]"
	dotnet build Core/Core.csproj -c $(CONFIG)
	@echo "$(GREEN)[✓]$(NC) Build complete"

.PHONY: format
format: ## Run dotnet format on the solution
	@echo "$(BLUE)[•]$(NC) Formatting solution..."
	dotnet format NextGen.slnx
	@echo "$(GREEN)[✓]$(NC) Format complete"

.PHONY: clean
clean: ## Remove all bin/ and obj/ directories (skips node_modules/.git)
	@echo "$(BLUE)[•]$(NC) Cleaning bin/ and obj/ directories..."
	@find . \( -name node_modules -o -name .git \) -prune \
		-o -type d \( -name bin -o -name obj \) -print \
		| xargs rm -rf
	@echo "$(GREEN)[✓]$(NC) Clean complete"

.PHONY: clean-output
clean-output: ## Remove the publish output directory
	@echo "$(BLUE)[•]$(NC) Removing $(OUTPUT)/..."
	rm -rf $(OUTPUT)
	@echo "$(GREEN)[✓]$(NC) $(OUTPUT) removed"

.PHONY: restore
restore: ## Restore NuGet packages
	@echo "$(BLUE)[•]$(NC) Restoring packages..."
	dotnet restore NextGen.slnx
	@echo "$(GREEN)[✓]$(NC) Restore complete"

.PHONY: format-check
format-check: ## Verify formatting without changes — fails if any file needs reformatting (mirrors CI)
	@echo "$(BLUE)[•]$(NC) Verifying code format..."
	dotnet format NextGen.slnx --verify-no-changes
	@echo "$(GREEN)[✓]$(NC) Format OK"

.PHONY: check
check: format-check build ## Run format-check + build — mirrors what CI does on every push
	@echo "$(GREEN)[✓]$(NC) All checks passed"

.PHONY: version
version: ## Print current version from charts/lampac/Chart.yaml
	@grep '^appVersion:' charts/lampac/Chart.yaml | awk '{print $$2}' | tr -d '"'

# ══════════════════════════════════════════════════════════════════════════════
#  Docker Compose — production (docker-compose.yaml)
# ══════════════════════════════════════════════════════════════════════════════

.PHONY: up
up: ## Start production compose stack (docker-compose.yaml) in the background
	docker compose -f $(COMPOSE_FILE) up -d

.PHONY: down
down: ## Stop and remove production compose stack
	docker compose -f $(COMPOSE_FILE) down

.PHONY: logs
logs: ## Tail logs of production compose stack (Ctrl-C to stop)
	docker compose -f $(COMPOSE_FILE) logs -f

.PHONY: ps
ps: ## Show status of production compose services
	docker compose -f $(COMPOSE_FILE) ps

# ── Docker Compose — dev (docker-compose.dev.yaml) ────────────────────────────

.PHONY: dev-setup
dev-setup: _dev-setup-dirs _dev-setup-config _dev-setup-plugins _dev-setup-passwd ## Create dev host dirs and copy example config
	@echo "$(GREEN)[✓]$(NC) Dev setup complete. Run 'make dev-up' when ready."

.PHONY: _dev-setup-dirs
_dev-setup-dirs:
	@echo "$(BLUE)[•]$(NC) Creating lampac-docker/config and lampac-docker/plugins..."
	@mkdir -p lampac-docker/config lampac-docker/plugins

.PHONY: _dev-setup-config
_dev-setup-config:
	@[ -f lampac-docker/config/development.init.conf ] \
		&& echo "$(YELLOW)[!]$(NC) lampac-docker/config/development.init.conf already exists — skipping copy" \
		|| { cp config/example.init.conf lampac-docker/config/development.init.conf; \
		     echo "$(GREEN)[✓]$(NC) Copied config/example.init.conf → lampac-docker/config/development.init.conf"; \
		     echo "$(YELLOW)[!]$(NC) Edit lampac-docker/config/development.init.conf (set listen.port=29118 for dev)"; }

.PHONY: _dev-setup-plugins
_dev-setup-plugins:
	@if [ ! -f lampac-docker/plugins/lampainit.js ]; then \
		touch lampac-docker/plugins/lampainit.js; \
		echo "$(GREEN)[✓]$(NC) Created empty lampac-docker/plugins/lampainit.js"; \
	fi

.PHONY: _dev-setup-passwd
_dev-setup-passwd:
	@if [ ! -f lampac-docker/config/passwd ]; then \
		echo "$(YELLOW)[!]$(NC) lampac-docker/config/passwd missing."; \
		echo "    Create it with: printf '%s' 'your_password' > lampac-docker/config/passwd"; \
	fi

.PHONY: dev-up
dev-up: ## Start dev compose stack (docker-compose.dev.yaml) in the background
	docker compose -f $(COMPOSE_DEV_FILE) up -d

.PHONY: dev-down
dev-down: ## Stop and remove dev compose stack
	docker compose -f $(COMPOSE_DEV_FILE) down

.PHONY: dev-logs
dev-logs: ## Tail logs of dev compose stack (Ctrl-C to stop)
	docker compose -f $(COMPOSE_DEV_FILE) logs -f

.PHONY: dev-ps
dev-ps: ## Show status of dev compose services
	docker compose -f $(COMPOSE_DEV_FILE) ps

# ══════════════════════════════════════════════════════════════════════════════
#  Docker build / push / cache
# ══════════════════════════════════════════════════════════════════════════════

.PHONY: docker-build
docker-build: _docker-check _docker-detect-platform ## Build image for the current platform and load into Docker
	docker buildx build --platform $(DETECTED_PLATFORM) --tag $(FULL_IMAGE) --file Dockerfile --load $(NO_CACHE_FLAG) .
	@echo "$(GREEN)[✓]$(NC) Build complete: $(FULL_IMAGE)"
	@docker images $(IMAGE_NAME):$(TAG) --format '  {{.Repository}}:{{.Tag}} — {{.Size}}'

.PHONY: _docker-detect-platform
_docker-detect-platform:
	$(eval DETECTED_PLATFORM := $(if $(PLATFORM),$(PLATFORM),$(shell arch=$$(uname -m); case $$arch in x86_64) echo linux/amd64 ;; arm64|aarch64) echo linux/arm64 ;; *) echo "Unsupported arch: $$arch" >&2; exit 1 ;; esac)))
	@echo "$(BLUE)[•]$(NC) Building $(FULL_IMAGE) [$(DETECTED_PLATFORM)]"

.PHONY: docker-build-amd64
docker-build-amd64: _docker-check _docker-setup-builder _docker-build-amd64-task ## Build linux/amd64 image and load into Docker

.PHONY: _docker-build-amd64-task
_docker-build-amd64-task:
	@echo "$(BLUE)[•]$(NC) Building $(FULL_IMAGE) [linux/amd64]"
	docker buildx build --builder $(BUILDER_NAME) --platform linux/amd64 --tag $(FULL_IMAGE) --file Dockerfile --load $(NO_CACHE_FLAG) .
	@echo "$(GREEN)[✓]$(NC) Build complete: $(FULL_IMAGE)"
	@docker images $(IMAGE_NAME):$(TAG) --format '  {{.Repository}}:{{.Tag}} — {{.Size}}'

.PHONY: docker-build-arm64
docker-build-arm64: _docker-check _docker-setup-builder _docker-build-arm64-task ## Build linux/arm64 image and load into Docker

.PHONY: _docker-build-arm64-task
_docker-build-arm64-task:
	@echo "$(BLUE)[•]$(NC) Building $(FULL_IMAGE) [linux/arm64]"
	docker buildx build --builder $(BUILDER_NAME) --platform linux/arm64 --tag $(FULL_IMAGE) --file Dockerfile --load $(NO_CACHE_FLAG) .
	@echo "$(GREEN)[✓]$(NC) Build complete: $(FULL_IMAGE)"
	@docker images $(IMAGE_NAME):$(TAG) --format '  {{.Repository}}:{{.Tag}} — {{.Size}}'

.PHONY: docker-build-all
docker-build-all: _docker-check _docker-setup-builder ## Build multi-arch image (amd64 + arm64); use PUSH=true to push
	@echo "$(BLUE)[•]$(NC) Building $(FULL_IMAGE) [$(ALL_PLATFORMS)]"
ifeq ($(PUSH),true)
	docker buildx build \
		--builder $(BUILDER_NAME) \
		--platform $(ALL_PLATFORMS) \
		--tag $(FULL_IMAGE) \
		--file Dockerfile \
		--push \
		$(NO_CACHE_FLAG) \
		.
	@echo "$(GREEN)[✓]$(NC) Multi-arch image pushed: $(FULL_IMAGE)"
else
	@echo "$(YELLOW)[!]$(NC) Multi-platform build without --push cannot be loaded locally."
	@echo "$(YELLOW)[!]$(NC) Image will be built but NOT loaded into Docker."
	@echo "$(YELLOW)[!]$(NC) Use PUSH=true to push to registry."
	docker buildx build \
		--builder $(BUILDER_NAME) \
		--platform $(ALL_PLATFORMS) \
		--tag $(FULL_IMAGE) \
		--file Dockerfile \
		--output type=image,push=false \
		$(NO_CACHE_FLAG) \
		.
	@echo "$(GREEN)[✓]$(NC) Multi-arch build complete (not loaded): $(FULL_IMAGE)"
	@echo ""
	@echo "To push: make docker-push-all TAG=$(TAG)"
endif

.PHONY: docker-push
docker-push: _docker-check ## Push current-platform image to registry (TAG=...)
	@echo "$(BLUE)[•]$(NC) Pushing $(FULL_IMAGE)..."
	docker push $(FULL_IMAGE)
	@echo "$(GREEN)[✓]$(NC) Pushed: $(FULL_IMAGE)"

.PHONY: docker-push-all
docker-push-all: _docker-check _docker-setup-builder ## Build AND push multi-arch image (amd64 + arm64)
	$(MAKE) docker-build-all PUSH=true TAG=$(TAG) NO_CACHE=$(NO_CACHE)

.PHONY: docker-export
docker-export: _docker-check ## Export single-platform image to tar (EXPORT_TAR=path PLATFORM=linux/amd64)
ifndef EXPORT_TAR
	$(error EXPORT_TAR is required, e.g. make docker-export EXPORT_TAR=./lampac.tar PLATFORM=linux/amd64)
endif
ifndef PLATFORM
	$(error PLATFORM is required for export, e.g. PLATFORM=linux/amd64)
endif
	@echo "$(BLUE)[•]$(NC) Exporting $(FULL_IMAGE) [$(PLATFORM)] → $(EXPORT_TAR)"
	docker buildx build \
		--platform $(PLATFORM) \
		--tag $(FULL_IMAGE) \
		--file Dockerfile \
		--output type=docker,dest=$(EXPORT_TAR) \
		$(NO_CACHE_FLAG) \
		.
	@echo "$(GREEN)[✓]$(NC) Exported:"
	@ls -lh $(EXPORT_TAR)

.PHONY: docker-clean-cache
docker-clean-cache: _docker-check _docker-cache-show-before _docker-cache-prune _docker-cache-show-after ## Clean buildx cache

.PHONY: _docker-cache-show-before
_docker-cache-show-before:
	@echo "$(BLUE)[•]$(NC) Builder cache before cleanup:"
	@docker buildx du --builder $(BUILDER_NAME) 2>/dev/null || true

.PHONY: _docker-cache-prune
_docker-cache-prune:
	@echo ""
	docker buildx prune --builder $(BUILDER_NAME) --force
	@echo "$(GREEN)[✓]$(NC) Builder $(BUILDER_NAME) cache cleared"

.PHONY: _docker-cache-show-after
_docker-cache-show-after:
	@echo ""
	@echo "$(BLUE)[•]$(NC) Cache after cleanup:"
	@docker system df 2>/dev/null || true

.PHONY: docker-clean-cache-all
docker-clean-cache-all: _docker-check _docker-clean-cache-all-confirm _docker-cache-show-after ## Clean ALL Docker build caches

.PHONY: _docker-clean-cache-all-confirm
_docker-clean-cache-all-confirm:
	@echo ""
	@echo "$(YELLOW)[!]$(NC) This will remove ALL Docker build cache (all builders, all projects)"
	@read -r -p "Are you sure? [y/N] " confirm; echo ""; if [ "$$confirm" = "y" ] || [ "$$confirm" = "Y" ]; then docker buildx prune --all --force; docker builder prune --force 2>/dev/null || true; echo "$(GREEN)[✓]$(NC) All Docker build cache cleared"; else echo "$(YELLOW)[!]$(NC) Skipped"; fi

.PHONY: docker-rm-builder
docker-rm-builder: ## Remove the lampac-builder buildx builder
	@docker buildx rm $(BUILDER_NAME) 2>/dev/null && \
		echo "$(GREEN)[✓]$(NC) Builder $(BUILDER_NAME) removed" || \
		echo "$(YELLOW)[!]$(NC) Builder $(BUILDER_NAME) not found"

# ══════════════════════════════════════════════════════════════════════════════
#  Helm
# ══════════════════════════════════════════════════════════════════════════════

# Shared extra-values flag (empty when HELM_VALUES is unset)
_HELM_VALUES_FLAG = $(if $(HELM_VALUES),-f $(HELM_VALUES),)

.PHONY: helm-lint
helm-lint: ## Lint the Helm chart (helm lint charts/lampac)
	@echo "$(BLUE)[•]$(NC) Linting Helm chart..."
	helm lint $(HELM_CHART) $(_HELM_VALUES_FLAG)
	@echo "$(GREEN)[✓]$(NC) Helm lint passed"

.PHONY: helm-template
helm-template: ## Render Helm chart templates to stdout (HELM_NAMESPACE, HELM_VALUES)
	helm template $(HELM_RELEASE) $(HELM_CHART) \
		--namespace $(HELM_NAMESPACE) \
		$(_HELM_VALUES_FLAG)

.PHONY: helm-dry-run
helm-dry-run: ## Dry-run helm upgrade --install (validates against a live cluster)
	@echo "$(BLUE)[•]$(NC) Helm dry-run for '$(HELM_RELEASE)' in namespace '$(HELM_NAMESPACE)'..."
	helm upgrade --install $(HELM_RELEASE) $(HELM_CHART) \
		--namespace $(HELM_NAMESPACE) --create-namespace \
		--dry-run \
		$(_HELM_VALUES_FLAG)

# ══════════════════════════════════════════════════════════════════════════════
#  Internal helpers (not shown in help)
# ══════════════════════════════════════════════════════════════════════════════

.PHONY: _docker-check
_docker-check: _docker-check-cmd _docker-check-daemon _docker-check-buildx

.PHONY: _docker-check-cmd
_docker-check-cmd:
	@command -v docker >/dev/null 2>&1 || (echo "$(RED)[✗]$(NC) Docker is not installed." >&2; exit 1)

.PHONY: _docker-check-daemon
_docker-check-daemon:
	@docker info >/dev/null 2>&1 || (echo "$(RED)[✗]$(NC) Docker daemon is not running. Start Docker Desktop." >&2; exit 1)

.PHONY: _docker-check-buildx
_docker-check-buildx:
	@docker buildx version >/dev/null 2>&1 || (echo "$(RED)[✗]$(NC) Docker Buildx is not available. Update Docker Desktop." >&2; exit 1)

.PHONY: _docker-setup-builder
_docker-setup-builder: _docker-setup-builder-check _docker-setup-builder-use

.PHONY: _docker-setup-builder-check
_docker-setup-builder-check:
	@if docker buildx inspect $(BUILDER_NAME) >/dev/null 2>&1; then echo "$(YELLOW)[!]$(NC) Builder '$(BUILDER_NAME)' already exists — reusing"; else echo "$(BLUE)[•]$(NC) Creating buildx builder '$(BUILDER_NAME)'..."; docker buildx create --name $(BUILDER_NAME) --driver docker-container --platform $(ALL_PLATFORMS) --bootstrap; echo "$(GREEN)[✓]$(NC) Builder '$(BUILDER_NAME)' created"; fi

.PHONY: _docker-setup-builder-use
_docker-setup-builder-use:
	@docker buildx use $(BUILDER_NAME)
