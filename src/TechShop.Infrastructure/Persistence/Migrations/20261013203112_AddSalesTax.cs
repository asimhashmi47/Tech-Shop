using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechShop.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesTax : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TaxAmount",
                table: "SalesItems",
                type: "REAL",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxTotal",
                table: "SalesInvoices",
                type: "REAL",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "IsTaxable",
                table: "Products",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxRate",
                table: "Products",
                type: "REAL",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TaxAmount",
                table: "SalesItems");

            migrationBuilder.DropColumn(
                name: "TaxTotal",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "IsTaxable",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "TaxRate",
                table: "Products");
        }
    }
}
