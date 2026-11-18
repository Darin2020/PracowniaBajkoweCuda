using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BajkoweCuda.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChatDigestIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_EmailOutbox_ToUserId_ConversationId",
                table: "EmailOutbox",
                columns: new[] { "ToUserId", "ConversationId" },
                unique: true,
                filter: "[Kind] = 'ChatDigest' AND [Status] = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmailOutbox_ToUserId_ConversationId",
                table: "EmailOutbox");
        }
    }
}
