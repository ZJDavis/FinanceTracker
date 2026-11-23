<#
.SYNOPSIS
    Generates synthetic ZD Enterprises transactions for FinanceTracker.

.DESCRIPTION
    Creates one stable JSON dataset covering October 1, 2025 through November 23, 2026.
    Transaction IDs are deterministic, so rerunning the importer will not duplicate rows.
#>
[CmdletBinding()]
param(
    [string] $OutputPath = (Join-Path $PSScriptRoot 'zd-enterprises-transactions.json'),
    [string] $OutputCsvPath = (Join-Path $PSScriptRoot 'zd-enterprises-transactions.csv'),
    [DateTime] $StartDate = [DateTime]::new(2025, 10, 1),
    [DateTime] $EndDate = [DateTime]::new(2026, 11, 23)
)

$ErrorActionPreference = 'Stop'
$transactions = [System.Collections.Generic.List[object]]::new()
$businessAccount = 'ZD Enterprises Operating'

function Add-DemoTransaction {
    param(
        [DateTime] $Date,
        [string] $Category,
        [string] $Payee,
        [string] $Memo,
        [decimal] $Amount
    )

    # A content-based GUID keeps regenerated copies of this dataset idempotent.
    $identityText = "$($Date.ToString('yyyy-MM-dd'))|$Category|$Payee|$Amount|$Memo"
    $identityBytes = [System.Text.Encoding]::UTF8.GetBytes("FinanceTracker:ZD-Enterprises:$identityText")
    $hashBytes = [System.Security.Cryptography.SHA256]::HashData($identityBytes)
    $syncId = [Guid]::new([byte[]] $hashBytes[0..15]).ToString('D')

    $transactions.Add([pscustomobject]@{
        Date = $Date.ToString('yyyy-MM-dd')
        AccountName = $businessAccount
        CategoryName = $Category
        Payee = $Payee
        Memo = $Memo
        Amount = $Amount
        SyncId = $syncId
    })
}

$employees = @('Staff 1', 'Staff 2', 'Staff 3', 'Staff 4', 'Staff 5')
for ($payDate = $StartDate; $payDate -le $EndDate; $payDate = $payDate.AddDays(1)) {
    if ($payDate.DayOfWeek -ne [DayOfWeek]::Friday) { continue }

    foreach ($employee in $employees) {
        Add-DemoTransaction $payDate 'Payroll' "ZD Enterprises payroll - $employee" 'Weekly gross pay; $1,800 per employee' ([decimal]-1800)
    }

    # Planning estimate only; actual employer taxes depend on location and payroll setup.
    Add-DemoTransaction $payDate 'Payroll Taxes' 'ZD Enterprises employer payroll taxes' 'Estimated employer payroll tax; 8.5% of weekly gross payroll' ([decimal]-765)
}

$monthlyRevenue = @(
    @{ Day = 2; Client = 'Northstar Health'; Amount = ([decimal]20000); Work = 'Software development retainer' },
    @{ Day = 9; Client = 'Silverline Logistics'; Amount = ([decimal]25000); Work = 'Operations consulting and integrations' },
    @{ Day = 16; Client = 'Copperline Finance'; Amount = ([decimal]25000); Work = 'Application modernization services' },
    @{ Day = 23; Client = 'Greenfield Retail'; Amount = ([decimal]30000); Work = 'Product engineering and support' }
)

$monthlyCosts = @(
    @{ Day = 1; Category = 'Office Rent'; Payee = 'ZD Enterprises office lease'; Memo = 'Monthly office rent'; Amount = ([decimal]-7200) },
    @{ Day = 3; Category = 'Software & Licenses'; Payee = 'Developer software licenses'; Memo = 'IDE, source control, design, and collaboration tools'; Amount = ([decimal]-1650) },
    @{ Day = 5; Category = 'Cloud Hosting'; Payee = 'Cloud hosting and backups'; Memo = 'Development, test, and production environments'; Amount = ([decimal]-2800) },
    @{ Day = 7; Category = 'Internet & Phone'; Payee = 'Business internet and phone'; Memo = 'Office connectivity and business mobile plans'; Amount = ([decimal]-420) },
    @{ Day = 10; Category = 'Insurance'; Payee = 'Business insurance provider'; Memo = 'General liability and professional indemnity coverage'; Amount = ([decimal]-1450) },
    @{ Day = 12; Category = 'Marketing'; Payee = 'ZD Enterprises marketing'; Memo = 'Website, lead generation, and client materials'; Amount = ([decimal]-2300) },
    @{ Day = 15; Category = 'Utilities'; Payee = 'Office utilities'; Memo = 'Electricity, water, and building services'; Amount = ([decimal]-780) },
    @{ Day = 18; Category = 'Accounting & Legal'; Payee = 'Bookkeeping and accounting'; Memo = 'Monthly bookkeeping and financial reporting'; Amount = ([decimal]-850) },
    @{ Day = 20; Category = 'Office Supplies'; Payee = 'Office supplies'; Memo = 'Printing, stationery, and small office purchases'; Amount = ([decimal]-350) },
    @{ Day = 25; Category = 'Bank Fees'; Payee = 'Payment processing fees'; Memo = 'Estimated 2% collection and merchant processing costs'; Amount = ([decimal]-2000) }
)

for ($month = [DateTime]::new($StartDate.Year, $StartDate.Month, 1); $month -le $EndDate; $month = $month.AddMonths(1)) {
    foreach ($invoice in $monthlyRevenue) {
        $date = [DateTime]::new($month.Year, $month.Month, $invoice.Day)
        if ($date -le $EndDate) {
            Add-DemoTransaction $date 'Consulting Revenue' "$($invoice.Client) - monthly services" $invoice.Work $invoice.Amount
        }
    }

    foreach ($cost in $monthlyCosts) {
        $date = [DateTime]::new($month.Year, $month.Month, $cost.Day)
        if ($date -le $EndDate) {
            Add-DemoTransaction $date $cost.Category $cost.Payee $cost.Memo $cost.Amount
        }
    }

    # Irregular costs make the ledger more representative than fixed monthly charges alone.
    if ($month.Month % 2 -eq 0) {
        Add-DemoTransaction $month.AddDays(21) 'Travel' 'Client travel and lodging' 'On-site discovery or project workshop' ([decimal]-1250)
    }
    if ($month.Month % 3 -eq 0) {
        Add-DemoTransaction $month.AddDays(26) 'Cloud Hosting' 'Cloud usage overage' 'Additional compute and storage for client workloads' ([decimal]-475)
    }
    if ($month.Month % 3 -eq 1) {
        Add-DemoTransaction $month.AddDays(27) 'Training' 'Technical training and certifications' 'Staff professional development' ([decimal]-900)
    }
}

$oneTimeCosts = @(
    @{ Date = [DateTime]::new(2025, 10, 17); Category = 'Equipment'; Payee = 'Developer laptops'; Memo = 'Five staff laptops for the business'; Amount = ([decimal]-7500) },
    @{ Date = [DateTime]::new(2025, 11, 14); Category = 'Accounting & Legal'; Payee = 'Business formation legal review'; Memo = 'Annual contract and entity review'; Amount = ([decimal]-1800) },
    @{ Date = [DateTime]::new(2026, 1, 30); Category = 'Equipment'; Payee = 'External monitors and docking stations'; Memo = 'Workstation equipment for five staff'; Amount = ([decimal]-2250) },
    @{ Date = [DateTime]::new(2026, 2, 13); Category = 'Security & Compliance'; Payee = 'Application security assessment'; Memo = 'Independent review for client delivery readiness'; Amount = ([decimal]-2400) },
    @{ Date = [DateTime]::new(2026, 4, 10); Category = 'Equipment'; Payee = 'Network and backup equipment'; Memo = 'Office router and encrypted backup storage'; Amount = ([decimal]-1350) },
    @{ Date = [DateTime]::new(2026, 6, 18); Category = 'Travel'; Payee = 'Software engineering conference'; Memo = 'Registration and travel for two staff'; Amount = ([decimal]-3600) },
    @{ Date = [DateTime]::new(2026, 8, 7); Category = 'Repairs & Maintenance'; Payee = 'Office equipment repair'; Memo = 'Repair of shared printer and meeting-room display'; Amount = ([decimal]-950) },
    @{ Date = [DateTime]::new(2026, 11, 12); Category = 'Taxes & Licenses'; Payee = 'Business licenses and registrations'; Memo = 'Annual renewals and local filing fees'; Amount = ([decimal]-1200) }
)

foreach ($cost in $oneTimeCosts) {
    if ($cost.Date -ge $StartDate -and $cost.Date -le $EndDate) {
        Add-DemoTransaction $cost.Date $cost.Category $cost.Payee $cost.Memo $cost.Amount
    }
}

# Quarterly estimated business-tax payments are illustrative cash outflows, not tax advice.
foreach ($date in @(
    [DateTime]::new(2025, 10, 15),
    [DateTime]::new(2026, 1, 15),
    [DateTime]::new(2026, 4, 15),
    [DateTime]::new(2026, 7, 15),
    [DateTime]::new(2026, 10, 15)
)) {
    if ($date -ge $StartDate -and $date -le $EndDate) {
        Add-DemoTransaction $date 'Taxes & Licenses' 'Estimated business tax payment' 'Illustrative quarterly estimated tax payment' ([decimal]-12000)
    }
}

$sortedTransactions = $transactions | Sort-Object Date, CategoryName, Payee
$json = ConvertTo-Json -InputObject @($sortedTransactions) -Depth 4
$outputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
[System.IO.File]::WriteAllText($OutputPath, $json + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))

# Use invariant decimal formatting so the CSV remains portable across locales.
function ConvertTo-CsvCell {
    param([string] $Value)
    return '"' + $Value.Replace('"', '""') + '"'
}

$csvLines = [System.Collections.Generic.List[string]]::new()
$csvLines.Add('Date,AccountName,CategoryName,Payee,Memo,Amount,SyncId')
foreach ($item in $sortedTransactions) {
    $amountText = ([decimal]$item.Amount).ToString('0.00', [System.Globalization.CultureInfo]::InvariantCulture)
    $csvLines.Add((@(
        (ConvertTo-CsvCell $item.Date),
        (ConvertTo-CsvCell $item.AccountName),
        (ConvertTo-CsvCell $item.CategoryName),
        (ConvertTo-CsvCell $item.Payee),
        (ConvertTo-CsvCell $item.Memo),
        $amountText,
        (ConvertTo-CsvCell $item.SyncId)
    ) -join ','))
}
$csvOutputDirectory = Split-Path -Parent $OutputCsvPath
New-Item -ItemType Directory -Path $csvOutputDirectory -Force | Out-Null
[System.IO.File]::WriteAllLines($OutputCsvPath, $csvLines, [System.Text.UTF8Encoding]::new($false))

Write-Host "Generated $($transactions.Count) synthetic transactions:"
Write-Host "  JSON: $OutputPath"
Write-Host "  CSV:  $OutputCsvPath"
