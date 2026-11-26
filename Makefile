# Testigo: the local demo stack (Docker Compose). Run `make` to list the targets.
#
#   make up         build and start Postgres, the API and the dashboards (in the background)
#   make down       stop everything (data in Postgres is kept)
#
# SERVICE picks which container `make logs` / `make restart` act on (default: all).

SERVICE ?=
COMPOSE := docker compose

.DEFAULT_GOAL := help
.PHONY: help env up run build down restart logs ps urls

help: ## List the targets
	@grep -E '^[a-zA-Z_-]+:.*## ' $(MAKEFILE_LIST) | awk 'BEGIN {FS = ":.*## "}; {printf "  \033[36m%-12s\033[0m %s\n", $$1, $$2}'

env: ## Create .env from .env.example if there isn't one (Mock model, no API key needed)
	@if [ ! -f .env ]; then cp .env.example .env && echo "Created .env from .env.example."; fi

up: env ## Build and start the stack in the background
	$(COMPOSE) up -d --build
	@$(MAKE) --no-print-directory urls

run: env ## Build and start the stack in the foreground, with logs (Ctrl+C stops it)
	$(COMPOSE) up --build

build: ## Rebuild the images without starting them
	$(COMPOSE) build

down: ## Stop the stack (data in Postgres is kept)
	$(COMPOSE) down

restart: ## Restart the containers (SERVICE=api for one)
	$(COMPOSE) up -d --force-recreate $(SERVICE)

logs: ## Follow the logs (SERVICE=api for one)
	$(COMPOSE) logs -f --tail=200 $(SERVICE)

ps: ## Show the containers
	@$(COMPOSE) ps

urls: ## Print where to open the demo
	@echo "Staff dashboard: http://localhost:3101  (admin / admin in the demo)"
	@echo "API:             http://localhost:5180"
