using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracra.Modules.Portfolio.Infrastructure.Migrations
{
    /// <summary>
    /// Brings the model snapshot into step with a column the node-tree attachment already added.
    /// </summary>
    /// <remarks>
    /// Deliberately empty, exactly as Activities' equivalent is. <c>access.attach_to_node_tree</c> created
    /// <c>node_ancestor_ids</c>, backfilled it, made it NOT NULL and put a trigger on it; mapping it on the
    /// aggregate so Strategy can ask for every item under a node is a model change with no schema change behind
    /// it. Letting EF emit its AddColumn instead would fail on every database that has already run the
    /// attachment — which is all of them.
    /// </remarks>
    public partial class MapPortfolioNodeColumns : Migration
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
