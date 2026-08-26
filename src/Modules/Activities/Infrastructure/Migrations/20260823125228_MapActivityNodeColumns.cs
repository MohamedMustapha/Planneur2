using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Activities.Infrastructure.Migrations
{
    /// <summary>
    /// Brings the model snapshot into step with columns the node-tree attachment already added.
    /// </summary>
    /// <remarks>
    /// Deliberately empty. <c>access.attach_to_node_tree</c> created <c>node_id</c> and <c>node_ancestor_ids</c>,
    /// backfilled them, made them NOT NULL and put a trigger on them; mapping them on the entity so the brief can
    /// group by node is a model change with no schema change behind it. Letting EF emit its AddColumn pair instead
    /// would fail on every database that has already run the attachment — which is all of them.
    /// </remarks>
    public partial class MapActivityNodeColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
