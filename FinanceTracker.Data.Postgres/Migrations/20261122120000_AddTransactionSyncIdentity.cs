using FinanceTracker.Data.Postgres;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FinanceTracker.Data.Postgres.Migrations;

[DbContext(typeof(FinanceDbContext))]
[Migration("20261122120000_AddTransactionSyncIdentity")]
public sealed class AddTransactionSyncIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "sync_id",
            table: "transactions",
            type: "uuid",
            nullable: false,
            defaultValueSql: "gen_random_uuid()");

        migrationBuilder.CreateIndex(
            name: "ux_transactions_sync_id",
            table: "transactions",
            column: "sync_id",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "ux_transactions_sync_id", table: "transactions");
        migrationBuilder.DropColumn(name: "sync_id", table: "transactions");
    }
}
