# Project Overview

**Last Verified:** `2026-11-26` | **Verified Commit:** `04392c875c275e99f4b5ee378f63b3a8b8fc541d`

## 1. Purpose
RetailFlow is a multi-tenant e-commerce SaaS product built as a **Modular Monolith** on **.NET 10 LTS** and **SQL Server**. Each merchant receives an isolated store identity, configuration, catalog, inventory, and order lifecycle on a shared application codebase (`Shared Database + Shared Schema + StoreId`). It is not a public multi-vendor marketplace.

## 2. Current State (As-Is) vs. Target State (To-Be)

| Capability | Current State (As-Is) | Target State (To-Be) | Gap |
|---|---|---|---|
| **Product Host** | Implemented in `src/RetailFlow.Api` (`ForwardedHeaders`, `AllowedHosts` validation, `ProblemDetails`, health checks, SPA fallback) | Same-Origin ASP.NET Core host serving React SPA + `/api/*` | React `wwwroot` assets not yet built |
| **Public Store Catalog** | Implemented (`GET /api/stores/{slug}/catalog/categories`, `/products`, `/products/{productId:guid}`) | Full storefront browsing + cart + checkout | Cart, Checkout, Orders not yet implemented |
| **Currency & StoreSettings** | Implemented (`Currencies` table with 8 ISO 4217 seeds, `StoreSettings` 1:1, `MoneyPrecision`, `ST-03` pickup rule) | Full merchant settings & branding management | Merchant write APIs & `StoreBranding` / `StoreAddress` pending |
| **Identity & Session Transport** | Implemented as isolated PoCs in `experiments/IdentitySpike/` & `experiments/SessionTransportSpike/` | Integrated `StoreCustomer` & `BackOffice` auth in `RetailFlow.Api` | `AUTH-ADR-01` still `Proposed`; not activated in production |
| **CI & Deployment** | GitHub Actions CI runs all 4 solutions against SQL Server 2022 + Playwright | Azure App Service Linux (`.NET 10`, OIDC) | Azure provisioning & deploy workflow not yet created |

## 3. Actors & Tenancy Model
- **Customer (As-Is public catalog / To-Be authenticated):** Browses an active store via `/api/stores/{slug}/catalog/...` without cross-store visibility.
- **Merchant Owner / Store Admin (To-Be):** Manages store settings, catalog, inventory, and orders.
- **Platform Admin (To-Be):** Reviews and approves/suspends stores.
- **System:** Enforces tenant isolation (`StoreId`), `MoneyPrecision`, and SQL composite foreign keys.

## 4. Glossary
- **StoreSlug:** Unique URL-safe store identifier resolved server-side for `StoreStatus.Active` stores.
- **StoreSettings:** One-to-one tenant configuration (`CurrencyCode`, `TimeZoneId`, `DeliveryEnabled`, `PickupEnabled`).
- **MinorUnit:** ISO 4217 decimal scale (`0` for JPY, `2` for USD/SAR/EUR/GBP/AED, `3` for JOD/KWD).
- **Available Inventory:** Derived quantity `OnHand - Reserved` (never stored as an independent mutable column).
