using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AlchemyCallbackTest.Forwarder.Migrations
{
    public partial class InitialForwarderSchema : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "forwarder");

            migrationBuilder.CreateTable(
                name: "raw_webhook_events",
                schema: "forwarder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EventData = table.Column<string>(type: "jsonb", nullable: false),
                    EventHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "NOW()"),
                    SourceIp = table.Column<string>(type: "inet", nullable: true),
                    Headers = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_raw_webhook_events", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_raw_webhook_events_received_at",
                schema: "forwarder",
                table: "raw_webhook_events",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "idx_raw_webhook_events_provider",
                schema: "forwarder",
                table: "raw_webhook_events",
                column: "Provider");

            migrationBuilder.CreateIndex(
                name: "idx_raw_webhook_events_event_type",
                schema: "forwarder",
                table: "raw_webhook_events",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "idx_raw_webhook_events_provider_hash",
                schema: "forwarder",
                table: "raw_webhook_events",
                columns: new[] { "Provider", "EventHash" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "raw_webhook_events",
                schema: "forwarder");
        }
    }
}
