using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alpixa.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class TemplateAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttachmentsJson",
                table: "Campaigns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TemplateAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TemplateId = table.Column<int>(type: "INTEGER", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    StoredFileName = table.Column<string>(type: "TEXT", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TemplateAttachments_Templates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "Templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TemplateAttachments_TemplateId",
                table: "TemplateAttachments",
                column: "TemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TemplateAttachments");

            migrationBuilder.DropColumn(
                name: "AttachmentsJson",
                table: "Campaigns");
        }
    }
}
