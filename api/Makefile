.PHONY: help up down logs ps build restore test run dev-api clean migrate-add migrate-update migrate-remove migrate-status

DC := docker compose
SRC_DIR := src
API_PROJ := $(SRC_DIR)/Bootstrapper/Pam.Api/Pam.Api.csproj
PROJ_OF_MODULE = $(SRC_DIR)/Modules/$(MODULE)/Pam.$(MODULE)/Pam.$(MODULE).csproj
DBCONTEXT_OF_MODULE = $(MODULE)DbContext

help:
	@echo "Available commands:"
	@echo "  make up                                       - Start docker-compose dependencies"
	@echo "  make down                                     - Stop docker-compose"
	@echo "  make logs SERVICE=mssql                       - Tail compose logs for a service"
	@echo "  make ps                                       - List compose services"
	@echo "  make restore                                  - dotnet restore"
	@echo "  make build                                    - dotnet build"
	@echo "  make test                                     - dotnet test"
	@echo "  make run                                      - Run Pam.Api with hot reload"
	@echo "  make dev-api                                  - Apply migrations, then watch (used by mprocs)"
	@echo "  make clean                                    - dotnet clean + remove bin/obj"
	@echo "  make migrate-add MODULE=Operators NAME=X       - Add an EF migration"
	@echo "  make migrate-update MODULE=Operators           - Apply migrations"
	@echo "  make migrate-remove MODULE=Operators           - Remove last migration"
	@echo "  make migrate-status MODULE=Operators           - List migrations"

up:
	$(DC) up -d

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

dev-api:
	@$(MAKE) migrate-update MODULE=Operators
	@dotnet watch --project $(API_PROJ)

clean:
	@dotnet clean
	@find . -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +

migrate-add:
ifndef MODULE
	$(error MODULE is required, e.g. make migrate-add MODULE=Operators NAME=InitialBrand)
endif
ifndef NAME
	$(error NAME is required, e.g. make migrate-add MODULE=Operators NAME=InitialBrand)
endif
	@dotnet ef migrations add $(NAME) \
		--project $(PROJ_OF_MODULE) \
		--startup-project $(API_PROJ) \
		--context $(DBCONTEXT_OF_MODULE) \
		--output-dir Data/Migrations

migrate-update:
ifndef MODULE
	$(error MODULE is required, e.g. make migrate-update MODULE=Operators)
endif
	@dotnet ef database update \
		--project $(PROJ_OF_MODULE) \
		--startup-project $(API_PROJ) \
		--context $(DBCONTEXT_OF_MODULE)

migrate-remove:
ifndef MODULE
	$(error MODULE is required, e.g. make migrate-remove MODULE=Operators)
endif
	@dotnet ef migrations remove \
		--project $(PROJ_OF_MODULE) \
		--startup-project $(API_PROJ) \
		--context $(DBCONTEXT_OF_MODULE)

migrate-status:
ifndef MODULE
	$(error MODULE is required, e.g. make migrate-status MODULE=Operators)
endif
	@dotnet ef migrations list \
		--project $(PROJ_OF_MODULE) \
		--startup-project $(API_PROJ) \
		--context $(DBCONTEXT_OF_MODULE)
