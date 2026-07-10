using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cart.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DurableIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Legacy rows only recorded that a key existed and cannot provide a safe replay response.
            migrationBuilder.DropTable(name: "idempotency_records");

            migrationBuilder.CreateTable(
                name: "idempotency_records",
                columns: table => new
                {
                    CartId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Operation = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResponseJson = table.Column<string>(type: "jsonb", nullable: false),
                    StatusCode = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_idempotency_records", x => new { x.CartId, x.Key }));

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_records_ExpiresAt",
                table: "idempotency_records",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "idempotency_records");

            migrationBuilder.CreateTable(
                name: "idempotency_records",
                columns: table => new
                {
                    CartId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_idempotency_records", x => new { x.CartId, x.Key }));
        }
    }
}
