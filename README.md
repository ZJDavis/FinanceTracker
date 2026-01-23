# FinanceTracker (WPF / MVVM)

**FinanceTracker** is a desktop personal finance and budget tracking application built with **C#**, **WPF**, and the **MVVM architectural pattern**.

The project is designed both as a practical personal finance tool and as a **portfolio-quality demonstration of MVVM, data binding, commands, filtering, and clean separation of concerns** in a real-world desktop application.

---

## 🚀 Features (Current)

### Transactions
- View transactions in a sortable, filterable grid
- Add, edit, and delete transactions
- Draft-based editor with **Save / Cancel** behavior
- Validation before saving (required fields, numeric amounts)
- Expenses and income handled consistently (expenses are negative)

### Filtering & Search
- Filter by:
  - Account
  - Month
  - Search text (payee / memo)
- Live filtering using `ICollectionView`

### Accounts & Categories
- Transactions are assigned to:
  - Accounts (e.g., Check
