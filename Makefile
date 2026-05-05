.PHONY: help up down logs ps build restore test run clean migrate-add migrate-update migrate-remove migrate-status

DC := docker compose
SRC_DIR := src
API_PROJ := $(SRC_DIR)/Bootstrapper/Pam.Api/Pam.Api.csproj
PROJ_OF_MODULE = $(SRC_DIR)/Modules/$(MODULE)/Pam.$(MODULE)/Pam.$(MODULE).csproj
DBCONTEXT_OF_MODULE = $(MODULE)DbContext

help:
	@echo "Available commands:"
	@echo "  make up                                   - Start docker-compose dependencies"
	@echo "  make down                                 - Stop docker-compose"
	@echo "  make logs SERVICE=postgres                - Tail compose logs for a service"
	@echo "  make ps                                   - List compose services"
	@echo "  make restore                              - dotnet restore"
	@echo "  make build                                - dotnet build"
	@echo "  make test                                 - dotnet test"
	@echo "  make run                                  - Run Pam.Api with hot reload"
	@echo "  make clean                                - dotnet clean + remove bin/obj"
	@echo "  make migrate-add MODULE=Player NAME=X     - Add an EF migration"
	@echo "  make migrate-update MODULE=Player         - Apply migrations"
	@echo "  make migrate-remove MODULE=Player         - Remove last migration"
	@echo "  make migrate-status MODULE=Player         - List migrations"

up:
	$(DC) up -d
	@echo "Waiting for Keycloak to be ready..."
	@until curl -sf http://localhost:8080/realms/master >/dev/null 2>&1; do sleep 2; done
	@./infra/keycloak/setup/declare-player-id.sh

down:
	$(DC) down

logs:
	$(DC) logs -f $(SERVICE)

ps:
	$(DC) ps

restore:
	@dotnet restore

build:
	@dotnet build

test:
	@dotnet test

run:
	@dotnet watch --project $(API_PROJ)

clean:
	@dotnet clean
	@find . -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +

migrate-add:
ifndef MODULE
	$(error MODULE is required, e.g. make migrate-add MODULE=Player NAME=InitialPlayer)
endif
ifndef NAME
	$(error NAME is required, e.g. make migrate-add MODULE=Player NAME=InitialPlayer)
endif
	@dotnet ef migrations add $(NAME) \
		--project $(PROJ_OF_MODULE) \
		--startup-project $(API_PROJ) \
		--context $(DBCONTEXT_OF_MODULE) \
		--output-dir Data/Migrations

migrate-update:
ifndef MODULE
	$(error MODULE is required, e.g. make migrate-update MODULE=Player)
endif
	@dotnet ef database update \
		--project $(PROJ_OF_MODULE) \
		--startup-project $(API_PROJ) \
		--context $(DBCONTEXT_OF_MODULE)

migrate-remove:
ifndef MODULE
	$(error MODULE is required, e.g. make migrate-remove MODULE=Player)
endif
	@dotnet ef migrations remove \
		--project $(PROJ_OF_MODULE) \
		--startup-project $(API_PROJ) \
		--context $(DBCONTEXT_OF_MODULE)

migrate-status:
ifndef MODULE
	$(error MODULE is required, e.g. make migrate-status MODULE=Player)
endif
	@dotnet ef migrations list \
		--project $(PROJ_OF_MODULE) \
		--startup-project $(API_PROJ) \
		--context $(DBCONTEXT_OF_MODULE)
