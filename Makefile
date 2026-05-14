.PHONY: help stress-up stress-down stress-api stress-reset stress-21g stress-fastspin

# Root-level Make targets. The .NET Makefile (build, test, migrations,
# dev-api) lives under api/. The targets here orchestrate cross-cutting
# workflows that span the compose stack + the k6 stress harness.

VUS ?= 50
DURATION ?= 90s
BASE_URL ?= http://localhost:5000

help:
	@echo "Stress targets:"
	@echo "  make stress-up         - docker compose up -d (mssql, rabbit, redis, otel-lgtm)"
	@echo "  make stress-down       - docker compose down"
	@echo "  make stress-api        - run Pam.Api in Stress mode (foreground)"
	@echo "  make stress-reset      - TRUNCATE ingest + outbox + audit tables"
	@echo "  make stress-21g        - run k6 against /v1/ingest/vendors/21g"
	@echo "  make stress-fastspin   - run k6 against /v1/ingest/vendors/fastspin/main"
	@echo "                           env: VUS=<n> DURATION=<k6 dur> BASE_URL=<url>"

stress-up:
	docker compose up -d mssql rabbitmq redis otel-lgtm seq

stress-down:
	docker compose down

# Run the API in Stress mode. Inherits Development bootstrap env vars so
# the Identity seeder is happy; appsettings.Stress.json overrides the
# rate limiter + reconciler timings. The API project is referenced by
# absolute path so this works from the repo root.
#
# --no-launch-profile is non-negotiable here. Without it, dotnet run uses
# launchSettings.json's `dev` profile whose environmentVariables block
# sets ASPNETCORE_ENVIRONMENT=Development, silently overriding the shell
# env var we set below. Symptom is a stress run where every request gets
# 429 because the rate-limit policy is still active.
stress-api:
	ASPNETCORE_ENVIRONMENT=Stress \
	PAM_BOOTSTRAP_OWNER_EMAIL=owner@test.local \
	PAM_BOOTSTRAP_OWNER_PASSWORD=OwnerPassword123! \
	dotnet run --no-launch-profile --project api/src/Bootstrapper/Pam.Api/Pam.Api.csproj --urls http://localhost:5000

# TRUNCATE the tables a stress run pollutes. Uses the sa account inside
# the running mssql container so we don't need sqlcmd on the host. The
# OUTBOX tables are FK-free, so TRUNCATE is safe.
#
# -I enables SET QUOTED_IDENTIFIER ON, required because MassTransit's
# outbox_message has filtered indexes that refuse DELETE under the
# legacy sqlcmd default (OFF). Same for any future indexed views.
stress-reset:
	@docker exec -i pam-mssql /opt/mssql-tools18/bin/sqlcmd \
		-S localhost -U sa -P 'Pam_dev_password_123!' -No -I -b -d pam -Q " \
			TRUNCATE TABLE ingest.vendor_transactions; \
			TRUNCATE TABLE messaging.outbox_dispatched_log; \
			DELETE FROM messaging.outbox_message; \
			TRUNCATE TABLE audit.command_log;"
	@echo "Reset: ingest.vendor_transactions, messaging.outbox_*, audit.command_log"

# Run the 21G stress scenario. Requires k6 on the host. Override VUS or
# DURATION via Make variables, e.g. `make stress-21g VUS=100 DURATION=3m`.
stress-21g:
	@command -v k6 >/dev/null 2>&1 || { echo "k6 not on PATH — brew install k6" >&2; exit 1; }
	k6 run \
		--env BASE_URL=$(BASE_URL) \
		--env VUS=$(VUS) \
		--env DURATION=$(DURATION) \
		tests/stress/21g.js

# Run the FastSpin (Kingdom Casino) intercept stress scenario. The API must
# be running in Stress mode so Stress:FastSpinUpstreamStub:Enabled swaps
# the GBS forward for a no-op fake — otherwise every request would hit
# the real dev GBS endpoint and dominate the numbers.
stress-fastspin:
	@command -v k6 >/dev/null 2>&1 || { echo "k6 not on PATH — brew install k6" >&2; exit 1; }
	k6 run \
		--env BASE_URL=$(BASE_URL) \
		--env VUS=$(VUS) \
		--env DURATION=$(DURATION) \
		tests/stress/fastspin.js
