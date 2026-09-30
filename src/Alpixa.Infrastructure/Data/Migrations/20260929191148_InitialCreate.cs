using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alpixa.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Campaigns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SenderProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    ContactListId = table.Column<int>(type: "INTEGER", nullable: false),
                    TemplateId = table.Column<int>(type: "INTEGER", nullable: false),
                    Subject = table.Column<string>(type: "TEXT", nullable: false),
                    HtmlBody = table.Column<string>(type: "TEXT", nullable: false),
                    TextBody = table.Column<string>(type: "TEXT", nullable: true),
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    Parallelism = table.Column<int>(type: "INTEGER", nullable: false),
                    DryRun = table.Column<bool>(type: "INTEGER", nullable: false),
                    TrackOpens = table.Column<bool>(type: "INTEGER", nullable: false),
                    HealthOverrideAccepted = table.Column<bool>(type: "INTEGER", nullable: false),
                    ScheduledUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NextRunUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    PauseReason = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Campaigns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContactLists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ConsentConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConsentConfirmedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactLists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EmailNormalized = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    CampaignId = table.Column<int>(type: "INTEGER", nullable: true),
                    Diagnostic = table.Column<string>(type: "TEXT", nullable: true),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    OccurredUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SenderProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    SmtpHost = table.Column<string>(type: "TEXT", nullable: false),
                    SmtpPort = table.Column<int>(type: "INTEGER", nullable: false),
                    Security = table.Column<int>(type: "INTEGER", nullable: false),
                    Auth = table.Column<int>(type: "INTEGER", nullable: false),
                    Username = table.Column<string>(type: "TEXT", nullable: false),
                    FromName = table.Column<string>(type: "TEXT", nullable: false),
                    FromAddress = table.Column<string>(type: "TEXT", nullable: false),
                    ReplyTo = table.Column<string>(type: "TEXT", nullable: true),
                    PostalAddress = table.Column<string>(type: "TEXT", nullable: true),
                    DailyLimit = table.Column<int>(type: "INTEGER", nullable: false),
                    HourlyLimit = table.Column<int>(type: "INTEGER", nullable: false),
                    PerMinuteLimit = table.Column<int>(type: "INTEGER", nullable: false),
                    DkimEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    DkimDomain = table.Column<string>(type: "TEXT", nullable: true),
                    DkimSelector = table.Column<string>(type: "TEXT", nullable: true),
                    DkimPrivateKeyPath = table.Column<string>(type: "TEXT", nullable: true),
                    ImapEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ImapHost = table.Column<string>(type: "TEXT", nullable: true),
                    ImapPort = table.Column<int>(type: "INTEGER", nullable: false),
                    ImapSecurity = table.Column<int>(type: "INTEGER", nullable: false),
                    ImapUsername = table.Column<string>(type: "TEXT", nullable: true),
                    ImapLastUid = table.Column<uint>(type: "INTEGER", nullable: false),
                    ImapLastCheckedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    WarmupEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    WarmupStartedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SenderProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SendJobs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CampaignId = table.Column<int>(type: "INTEGER", nullable: false),
                    ContactId = table.Column<long>(type: "INTEGER", nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: false),
                    RecipientDomain = table.Column<string>(type: "TEXT", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    SmtpCode = table.Column<int>(type: "INTEGER", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", nullable: true),
                    MessageId = table.Column<string>(type: "TEXT", nullable: true),
                    SentUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SendJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Suppressions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EmailNormalized = table.Column<string>(type: "TEXT", nullable: false),
                    Reason = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suppressions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Templates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Subject = table.Column<string>(type: "TEXT", nullable: false),
                    HtmlBody = table.Column<string>(type: "TEXT", nullable: false),
                    TextBody = table.Column<string>(type: "TEXT", nullable: true),
                    IsBuiltIn = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Templates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Contacts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ListId = table.Column<int>(type: "INTEGER", nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: false),
                    EmailNormalized = table.Column<string>(type: "TEXT", nullable: false),
                    Domain = table.Column<string>(type: "TEXT", nullable: false),
                    FirstName = table.Column<string>(type: "TEXT", nullable: true),
                    LastName = table.Column<string>(type: "TEXT", nullable: true),
                    Company = table.Column<string>(type: "TEXT", nullable: true),
                    CustomFieldsJson = table.Column<string>(type: "TEXT", nullable: true),
                    ConsentSource = table.Column<string>(type: "TEXT", nullable: true),
                    ConsentDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Flags = table.Column<int>(type: "INTEGER", nullable: false),
                    SoftBounceCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Contacts_ContactLists_ListId",
                        column: x => x.ListId,
                        principalTable: "ContactLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_Status",
                table: "Campaigns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_Domain",
                table: "Contacts",
                column: "Domain");

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_EmailNormalized",
                table: "Contacts",
                column: "EmailNormalized");

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_ListId_EmailNormalized",
                table: "Contacts",
                columns: new[] { "ListId", "EmailNormalized" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_ListId_Status",
                table: "Contacts",
                columns: new[] { "ListId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryEvents_CampaignId_Kind",
                table: "DeliveryEvents",
                columns: new[] { "CampaignId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryEvents_EmailNormalized",
                table: "DeliveryEvents",
                column: "EmailNormalized");

            migrationBuilder.CreateIndex(
                name: "IX_SendJobs_CampaignId_ContactId",
                table: "SendJobs",
                columns: new[] { "CampaignId", "ContactId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SendJobs_CampaignId_Status_Sequence",
                table: "SendJobs",
                columns: new[] { "CampaignId", "Status", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_SendJobs_Email",
                table: "SendJobs",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_SendJobs_MessageId",
                table: "SendJobs",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_SendJobs_SentUtc",
                table: "SendJobs",
                column: "SentUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Suppressions_EmailNormalized",
                table: "Suppressions",
                column: "EmailNormalized",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Campaigns");

            migrationBuilder.DropTable(
                name: "Contacts");

            migrationBuilder.DropTable(
                name: "DeliveryEvents");

            migrationBuilder.DropTable(
                name: "SenderProfiles");

            migrationBuilder.DropTable(
                name: "SendJobs");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "Suppressions");

            migrationBuilder.DropTable(
                name: "Templates");

            migrationBuilder.DropTable(
                name: "ContactLists");
        }
    }
}
