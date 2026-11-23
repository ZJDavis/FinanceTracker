# CSV transaction import

FinanceTracker can import transactions from a CSV file in either SQLite or API mode. Choose **Import CSV** in the main window, review the preview, select the valid rows to keep, and confirm. The import uses the active data client, so API-mode transactions go through the API and its normal event/outbox flow.

## File format

Required column headers are `Date`, `AccountName`, `CategoryName`, `Payee`, `Memo`, and `Amount`. The optional `SyncId` column carries a stable transaction GUID. Dates use `YYYY-MM-DD`; amounts use a period as the decimal separator, with expenses negative and income positive. Standard quoted CSV cells are supported.

Example:

```csv
Date,AccountName,CategoryName,Payee,Memo,Amount,SyncId
2026-11-02,"ZD Enterprises Operating","Consulting Revenue","Northstar Health - monthly services","Software development retainer",20000.00,3a4ea58e-e44a-5b69-865e-2cc93a9459a7
```

Account and category names are matched without regard to letter case against the active database. The category must have the right kind for the amount: positive amounts need an income category, and negative amounts need an expense category. The seeded `ZD Enterprises Operating` account and business categories make the included sample ledger ready to import.

## Preview and duplicate handling

The preview reports each row as ready, a possible duplicate, or invalid. It checks dates, amounts, required payees, account/category matches, duplicate transaction content, and repeated `SyncId` values. Invalid rows are disabled. Possible duplicates are unchecked by default, but the user can explicitly include one after reviewing it. Rows not selected are left untouched.

Rows are saved one at a time through the selected finance client. If an individual save fails, the import continues and the completion message reports successful and failed counts. The `SyncId` is preserved when it is provided; a GUID is generated for files that omit the optional column. Re-importing the generated sample is therefore recognized as duplicate content/identity.

## Included sample

`data/demo/zd-enterprises-transactions.csv` is generated alongside the JSON seed data by `data/demo/Generate-ZdEnterprisesDataset.ps1`. It contains synthetic records only. Payroll and estimated business taxes are illustrative and are not tax guidance.

The sample has more than 100 rows, so a spreadsheet editor is not required to try the feature. The script `scripts/import-zd-enterprises.ps1` remains available to seed SQLite directly without opening the desktop UI.

## Current scope

This first import flow is for transaction CSV files and a known account/category list. It does not provide custom column mapping, create accounts/categories, import batches/rollback, or local-to-PostgreSQL database synchronization. Database synchronization, conflict resolution, and close-time convergence remain TODO work described in [FINANCETRACKER_BACKEND_DESIGN.md](../FINANCETRACKER_BACKEND_DESIGN.md).
