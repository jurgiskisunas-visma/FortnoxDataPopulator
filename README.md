# FortnoxConsoleApp

A small .NET console tool for seeding **test customer and supplier invoices** into a [Fortnox](https://www.fortnox.se/) company via the official [Fortnox.NET.SDK](https://www.nuget.org/packages/Fortnox.NET.SDK). Useful for populating a sandbox / test company with realistic-looking data so you can exercise integrations, reports, or migrations without hand-crafting every record.

Invoices are generated with randomised (but Swedish-looking) names, addresses and line items, dated inside the current financial year, and then **bookkept** automatically so they show up as posted vouchers — not drafts.

---

## What it does

When you run the app you get a small menu:

```
Choose an option:
  1) Add customer invoice(s)
  2) Add supplier invoice(s)
  3) Add both (customer + supplier)
  r) Re-authenticate (clears cached token)
  q) Quit
```

For each option, you pick a count and the tool will:

1. Ensure the required bookkeeping accounts exist (`3001` revenue, `4000` expense, `2440` AP) — creating them if the chart of accounts doesn't have them yet.
2. Ensure the predefined voucher series (`Invoice` / `SupplierInvoice`) point at a real series (`C`/`F`/`A`), so posting will succeed.
3. Create a fresh customer or supplier with a randomly generated name.
4. Create N invoices against that customer/supplier, dated inside the active financial year.
5. Call the Fortnox **Bookkeep** endpoint on each invoice and report `posted` / `NOT posted: <reason>`.
6. Print a summary with total elapsed time.

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A [Fortnox developer account](https://developer.fortnox.se/) and an **integration** (API app) registered there.
- A Fortnox company you are allowed to write data into (use a **test / sandbox company** — this tool mutates data).

### Register a Fortnox integration

1. Log into the [Fortnox Developer Portal](https://developer.fortnox.se/) and create an integration.
2. Grant it the scopes the app requests (bookkeeping, costcenter, project, companyinformation, customer, supplier, article, invoice, payment, supplierinvoice, settings).
3. Set a **Redirect URI** — the default this app uses is:

   ```
   http://localhost:5016/api/fortnox/connect
   ```

   You can change this (see config below), but the value in your Fortnox integration and in `appsettings.local.json` **must match exactly**.
4. Copy the **Client ID** and **Client Secret**.

---

## Configuration

Secrets are read from `appsettings.local.json`, which is **gitignored**. Create it next to `appsettings.json`:

```json
{
  "Fortnox": {
    "ClientId": "your-client-id",
    "ClientSecret": "your-client-secret",
    "RedirectUri": "http://localhost:5016/api/fortnox/connect"
  }
}
```

The committed `appsettings.json` contains placeholder values and serves as a template — don't put real credentials in it.

### Where tokens are cached

After the first successful login the app stores the OAuth access + refresh token on disk so subsequent runs don't re-prompt:

- **Windows:** `%LOCALAPPDATA%\FortnoxConsoleApp\tokens.json`
- **Linux / macOS:** `~/.local/share/FortnoxConsoleApp/tokens.json`

Pick `r` in the menu (or delete that file) to force a fresh browser login.

---

## Running

```bash
dotnet run --project FortnoxConsoleApp.csproj
```

On first launch the app opens your browser to Fortnox, you approve the connection against the company you want to populate, and the app captures the callback on `localhost` automatically. Then the menu appears.

Example session:

```
Fortnox Console App
===================
Opening browser to authorize with Fortnox...
Authorization code received — exchanging for tokens...

Authenticated. Ready to create invoices.

Choose an option:
  1) Add customer invoice(s)
  2) Add supplier invoice(s)
  3) Add both (customer + supplier)
> 3
How many of each (customer + supplier) [1]: 5

--- Creating 5 customer invoice(s) for "Grumpy Narwhal AB" ---
  Using revenue account 3001.
  Financial year: 2026-01-01 to 2026-12-31 (contains today).
  Available voucher series: A, B, C, F, I, L, M
  Invoice: using existing default 'C'.
  SupplierInvoice: using existing default 'F'.
Customer created: #42 — Grumpy Narwhal AB
[10:12:03][1/5] invoice #7 - 375.00 SEK (posted)
...
Done: 5/5 customer invoices created, 5/5 posted, in 4.2 s.
```

---

## Project layout

```
FortnoxConsoleApp/
├── FortnoxConsoleApp.slnx          Solution (classic .sln format also works)
├── FortnoxConsoleApp.csproj        .NET 10 exe project
├── Program.cs                      Entrypoint + interactive menu
├── appsettings.json                Committed template (placeholders only)
├── appsettings.local.json          Your real secrets (gitignored)
├── NuGet.Config                    Pins the nuget.org feed
└── Services/
    ├── FortnoxAuthService.cs       OAuth2 flow: spins up a local HTTP listener,
    │                               opens the browser, exchanges code for tokens,
    │                               caches + refreshes on subsequent runs.
    ├── AccountResolver.cs          Ensures chart-of-accounts entries 3001/4000/2440 exist.
    ├── VoucherSeriesResolver.cs    Ensures predefined voucher series are set to real codes.
    ├── FinancialYearInfo.cs        Finds a usable FY and clamps invoice dates into it.
    ├── InvoiceCreator.cs           Builds + creates + bookkeeps customer & supplier invoices.
    └── NameGenerator.cs            Silly random names, services, and comments.
```

---

## Troubleshooting

**"Could not bind TCP listener on port 5016."**
Another process is using the port, or the `RedirectUri` you configured doesn't match what's registered on your Fortnox integration. Free the port or pick a different one in *both* places.

**"No financial years configured in this company."**
Open the Fortnox UI for the target company and set up a financial year first — posting requires one.

**"NOT posted: ..."**
The invoice was created but Fortnox refused to book it. Common causes: the voucher series for the relevant type isn't set, the chart of accounts doesn't include the accounts this app uses, or the financial year is locked. The app prints the Fortnox error message alongside; that's usually enough to diagnose.

**Refresh token is rejected.**
Pick `r` in the menu, or delete `tokens.json` (see path above) and re-authorize.

---

## License

MIT — do whatever you want with it, no warranty. This is a disposable developer tool, not production software.
