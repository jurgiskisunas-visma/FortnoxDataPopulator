# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Interactive .NET 10 console tool that seeds **test customer and supplier invoices** into a Fortnox company via OAuth2 and the `Fortnox.NET.SDK` (v4.4.1). It is a developer tool for populating sandbox / test companies — not production software. Invoices are always both **created and bookkept**; drafts are treated as failure.

## Commands

```bash
# Build (slnx is the only solution file; dotnet CLI 9+ / VS 2022 17.10+ / Rider handle it)
dotnet build FortnoxDataPopulator.slnx

# Run
dotnet run --project FortnoxDataPopulator.csproj

# Restore (rarely needed explicitly — build auto-restores)
dotnet restore
```

There is no test project and no linter configured. `dotnet build` with warnings-as-errors is the only quality gate.

## Configuration model

Two JSON files, same schema, layered at startup in `Program.cs`:

1. `appsettings.json` — committed, contains **placeholder** values (`YOUR_FORTNOX_CLIENT_ID` etc). Treat it as a template; never commit real secrets here.
2. `appsettings.local.json` — **gitignored**, holds the real `ClientId` / `ClientSecret` / `RedirectUri`. Its presence is optional at the `ConfigurationBuilder` level (`optional: true`) but required to actually authenticate.

The redirect URI is hard-dependent on a free local TCP port (default 5016). It must match the URI registered in the Fortnox developer portal exactly — including path and port — or the OAuth callback will 404.

## Architecture — the create-and-post pipeline

Understanding `InvoiceCreator.AddCustomerInvoicesAsync` / `AddSupplierInvoicesAsync` requires seeing how the helpers co-operate. Each batch runs this pipeline in order:

1. **`FinancialYearInfo.GetUsableAsync`** — finds the FY covering today, or falls back to the latest FY. Returns `null` if the company has no FY at all (posting will fail; the code warns but still tries). `FinancialYearInfo.Clamp` is then used to force every invoice date inside the chosen FY — **don't bypass it**, Fortnox rejects invoices dated outside any FY.
2. **`AccountResolver.ResolveAsync`** — ensures the Swedish BAS accounts `3001` (revenue), `4000` (expense), `2440` (accounts payable) exist in the chart of accounts, creating them if missing. "Already exists" is detected by message text (Swedish "existerar redan" or English "already exists") because the Fortnox API doesn't expose a stable error code for it.
3. **`VoucherSeriesResolver.EnsureDefaultsAsync`** — Fortnox posting requires a *predefined voucher series* to be mapped for each document type. This step reads the company's actual voucher series codes and, if the predefined mapping for `Invoice` / `SupplierInvoice` doesn't point at one of them, sets it to the preferred code (`C` for customer invoices, `F` for supplier, `A` as fallback). Without this the Bookkeep endpoint fails with an opaque error.
4. **Customer/Supplier creation** — one new entity per batch, with a randomly generated name from `NameGenerator`. This is the only place customer/supplier records are created; invoices in the same batch all reference it.
5. **Invoice creation + bookkeeping** — `client.InvoiceConnector.CreateAsync(...)` then `BookkeepAsync(documentNumber)`. A created-but-not-bookkept invoice counts as a partial failure in the summary output ("posted" vs "NOT posted: <reason>").

### Customer vs supplier invoice structure (important quirk)

- **Customer invoices** (`BuildCustomerInvoice`) explicitly list 1–3 `InvoiceRow`s with `AccountNumber = 3001`, a quantity, price, and 25% VAT. Fortnox computes totals.
- **Supplier invoices** (`BuildSupplierInvoice`) have a different contract: set `Total` explicitly, provide **only the expense (debit) row** against account 4000, and let Fortnox auto-generate the balancing accounts-payable (credit) row from the supplier's default AP account. See the comment at `Services/InvoiceCreator.cs` around line 295 — adding a manual AP row here duplicates it and breaks the debit/credit balance, which surfaces as a posting error downstream, not a creation error. Preserve this asymmetry.

## OAuth flow

`FortnoxAuthService` implements a single-use loopback OAuth2 handshake without any web framework:

- Builds the authorize URL via `Fortnox.SDK.Authorization.FortnoxAuthClient.StandardAuthWorkflow.BuildAuthUri`, opens the user's browser, and listens on a bare `TcpListener` for the redirect.
- Parses the HTTP request line manually (no ASP.NET), extracts `code` + `state`, verifies state for CSRF, then exchanges the code.
- Caches tokens at `%LOCALAPPDATA%\FortnoxDataPopulator\tokens.json` (Linux/macOS: `~/.local/share/FortnoxDataPopulator/tokens.json`). On next start, the cached access token is reused if it has >2 min left, else the refresh token is used, else a full browser flow runs again.
- The menu's `r` option calls `ResetTokens()` to delete the cache and force a fresh browser login.

If you change the requested OAuth scopes in `RunBrowserAuthorizationAsync`, users must re-authorize — refresh tokens don't auto-upgrade.

## Style conventions visible in the code

- `namespace X;` file-scoped, `Nullable` + `ImplicitUsings` enabled.
- `using` directives placed **inside** the namespace block.
- `sealed` on private/internal classes that aren't explicitly designed for inheritance.
- `this.` qualifier used on instance member access (e.g. `FortnoxAuthService`).
- Target framework is `net10.0` with `LangVersion` pinned to `13.0`.
