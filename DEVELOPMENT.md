# Development and verification

Open `FinanceTracker.sln`. The Core and Data projects live inside this folder;
the WPF project excludes their source trees and the Tests tree from its own compilation.

The app targets .NET 8. Install its SDK and desktop runtime, or install Visual Studio
with the .NET desktop development workload and .NET 8 targeting support.

From this directory:

```powershell
./verify.ps1
```

The verification script uses the workspace-local SDK downloaded for this repair
when available, otherwise the installed `dotnet` command. With a system SDK installed,
the individual commands are:

```powershell
dotnet build FinanceTracker.sln
dotnet test Tests/Unit/FinanceTracker.UnitTests.csproj
dotnet run --project Tests/FinanceTracker.Regression.csproj
dotnet run --project FinanceTracker.Wpf.csproj
```

The regression executable uses an STA thread for WPF collection views. It returns
a nonzero exit code on failure and covers draft cancellation, save insertion,
edit cancellation, deletion totals/selection, filtering, invalid drafts, and
regional decimal input. It also instantiates and lays out the main window and checks
that its bindings preserve a new draft. It also checks SQLite reopening, draft cancellation,
and failed writes. The xUnit project is included in the solution for Test Explorer and tests
editor validation and the database. These checks do not perform a
complete visual/manual UI review.

New drafts stay outside the saved collection until Save succeeds. Selecting another
row discards unsaved editor changes. New drafts start in the selected month. Amount
input uses the current regional decimal separator without grouping separators.
The displayed total is filtered net activity, not an account balance.
Amounts retain decimal precision rather than enforcing two decimal places; zero is allowed.
An overflowing total displays a warning instead of crashing or displaying a wrapped number.
See SECURITY.md for the SQL parameterization boundary and injection-test results.

## Verification status

On October 7, 2026, the solution built with zero warnings and errors. All 43
xUnit tests and all 17 STA regression checks passed. In Visual Studio, open
Test > Test Explorer to run the xUnit tests; `verify.ps1` runs both suites.

SQLite uses `%LOCALAPPDATA%\FinanceTracker\finance.db`. The directory and schema
are created at first launch. Starter accounts/categories are inserted once with
zero opening balances; no sample transactions are inserted into the database.
The parameterless view model remains an in-memory demo for regression tests.

Schema creation and versioning are transactional (`PRAGMA user_version = 2`).
Foreign keys are enabled on every connection. Dates use ISO text and monetary
values use invariant decimal text to preserve precision. IDs are database-generated. Version-1 databases migrate without dropping their data.
Version checks reject stale updates and UI deletions from another app instance.
Save and Delete commit to SQLite before changing displayed rows. Failed writes
leave the draft or row available and show an error; startup failures stop the app
without replacing the database. Each operation opens and closes its connection.

You can back up `finance.db` while the application is closed. Automated tests use
unique temporary databases and do not open your real finance database.

## Next feature milestones

1. Account/category management and balances calculated from opening balances and
   all account transactions. Keep balances separate from filtered activity.
2. Transfers with paired account entries excluded from income/expense summaries.
3. CSV import preview, duplicate handling, export, and database backups.
4. Monthly summaries, then category budgets and charts.

Before larger UI changes, add unsaved-change handling for selection and shutdown,
an explicit All accounts option.
