# Macro Deck development tasks. Each target is one step in tools/dev/tasks.sh; "make help" lists them.
# Options: CONFIGURATION=Release BUILD_CHANNEL=Production E2E_SUITE=all|smoke E2E_FILTER=<file>

TASKS := ./tools/dev/tasks.sh
.DEFAULT_GOAL := help
.NOTPARALLEL:
export CONFIGURATION BUILD_CHANNEL E2E_SUITE E2E_FILTER

.PHONY: help install dev dev-ui dev-app host host-web-client desktop-ui web-client dev-reset \
	ci test build-host test-host test-conformance test-platform test-packages \
	test-ui test-ui-scripts test-runtime test-web-client test-desktop-ui build-ui \
	test-notices test-bootstrapper test-e2e test-e2e-smoke docs

help: ## Show this list
	@$(TASKS) help

##@ Setup

install: ## Install the ui/ npm workspace
	@$(TASKS) install

##@ Development

dev: ## Host + desktop UI (:4200) + web client (:7193)
	@$(TASKS) dev

dev-ui: ## Host + desktop UI (:4200)
	@$(TASKS) dev-ui

dev-app: ## Host + desktop UI in the Tauri window
	@$(TASKS) dev-app

host: ## Only the host (loopback :5191, public :7193)
	@$(TASKS) host

host-web-client: ## Only the host, serving the built web client
	@$(TASKS) host-web-client

desktop-ui: ## Only the desktop UI dev server
	@$(TASKS) desktop-ui

web-client: ## Rebuild the web client
	@$(TASKS) web-client

dev-reset: ## Delete .data (next start is a first run)
	@$(TASKS) dev-reset

##@ CI: everything

ci: ## Every PR check that runs on this OS
	@$(TASKS) ci

test: ## All build and test jobs, no packaging or E2E
	@$(TASKS) test

##@ CI: .NET

build-host: ## dotnet build with warnings as errors
	@$(TASKS) build-host

test-host: ## Host build & tests
	@$(TASKS) test-host

test-conformance: ## SDK conformance tests
	@$(TASKS) test-conformance

test-platform: ## Host tests for this OS
	@$(TASKS) test-platform

test-packages: ## NuGet pack and verify
	@$(TASKS) test-packages

test-notices: ## Third-party notices up to date
	@$(TASKS) test-notices

##@ CI: UI

test-ui: ## All UI tests and builds
	@$(TASKS) test-ui

test-ui-scripts: ## Icon checks and script tests
	@$(TASKS) test-ui-scripts

test-runtime: ## Runtime build, ES5 gate, tests
	@$(TASKS) test-runtime

test-web-client: ## Web client build, tests, compatibility gate
	@$(TASKS) test-web-client

test-desktop-ui: ## desktop-ui specs and layering checks
	@$(TASKS) test-desktop-ui

build-ui: ## Production builds and device targets
	@$(TASKS) build-ui

##@ CI: bootstrapper and E2E

test-bootstrapper: ## cargo fmt, clippy, test
	@$(TASKS) test-bootstrapper

test-e2e: ## Core E2E against a staged production host
	@$(TASKS) test-e2e

test-e2e-smoke: ## Core E2E, @smoke subset
	@$(TASKS) test-e2e-smoke

##@ Docs

docs: ## Build the documentation site
	@$(TASKS) docs
