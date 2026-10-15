// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) Entity Framework Core documentation.
// =============================================================================

using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TheeBuhle.Infrastructure.Persistence;

/// <summary>
/// Model-wide conventions: column naming and the two value converters.
/// </summary>
internal static class ModelConventions
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Stores a list of short strings as one JSON array in a single column.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Used for a check-in's support areas and a volunteer's skills. Both are
    /// short closed vocabularies that are never joined on and never aggregated
    /// — the safeguarding queue filters on a flag, not on the areas — so a table
    /// and a join would cost a migration and buy nothing.
    /// </para>
    /// <para>
    /// The moment either one needs to be queried relationally ("how many
    /// concerns mentioned transport this term?") this becomes the wrong choice
    /// and should become a table. Recorded here so that decision is made
    /// deliberately rather than discovered.
    /// </para>
    /// </remarks>
    public static ValueConverter<List<string>, string> StringListConverter { get; } =
        new(list => JsonSerializer.Serialize(list, Json),
            json => JsonSerializer.Deserialize<List<string>>(json, Json) ?? new List<string>());

    /// <summary>
    /// Tells EF how to detect a change inside the list.
    /// </summary>
    /// <remarks>
    /// Required, not optional. Without a comparer EF compares the two
    /// <see cref="List{T}"/> instances by reference, so adding an item to an
    /// existing list is <em>not</em> seen as a change and the update is silently
    /// dropped. A converted collection with no comparer is one of the easiest
    /// ways to lose a write in EF Core.
    /// </remarks>
    public static ValueComparer<List<string>> StringListComparer { get; } =
        new((left, right) => left != null && right != null
                ? left.SequenceEqual(right, StringComparer.Ordinal)
                : left == right,
            list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode(StringComparison.Ordinal))),
            list => new List<string>(list));

    /// <summary>
    /// Stores a volunteer's availability grid as one JSON object.
    /// </summary>
    /// <remarks>
    /// "Saturday: mornings; Wednesday: evenings" — read whole by one staff
    /// member when they schedule an induction, and never aggregated over. The
    /// relational alternative is a table with three columns and no queries.
    /// </remarks>
    public static ValueConverter<Dictionary<string, List<string>>, string> AvailabilityConverter { get; } =
        new(grid => JsonSerializer.Serialize(grid, Json),
            json => JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json, Json)
                ?? new Dictionary<string, List<string>>());

    /// <summary>
    /// Change detection for the availability grid.
    /// </summary>
    /// <remarks>
    /// Serialises both sides and compares the text. Slower than a structural
    /// walk and completely adequate for a dictionary of at most seven keys that
    /// is written once per application — and it cannot get the nested list
    /// comparison subtly wrong, which a hand-written walk can.
    /// </remarks>
    public static ValueComparer<Dictionary<string, List<string>>> AvailabilityComparer { get; } =
        new((left, right) => JsonSerializer.Serialize(left, Json) == JsonSerializer.Serialize(right, Json),
            grid => JsonSerializer.Serialize(grid, Json).GetHashCode(StringComparison.Ordinal),
            grid => JsonSerializer.Deserialize<Dictionary<string, List<string>>>(
                JsonSerializer.Serialize(grid, Json), Json)!);

    /// <summary>
    /// Renames every table and column to snake_case.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Applied as a convention rather than by annotating 40 entities, so a new
    /// property cannot arrive with a PascalCase column name that nothing notices
    /// until somebody writes SQL by hand.
    /// </para>
    /// <para>
    /// It also keeps this schema readable next to the SQL in the Task 1
    /// documentation and the seed scripts, which are written snake_case
    /// throughout.
    /// </para>
    /// </remarks>
    public static void UseSnakeCaseNames(ModelBuilder model)
    {
        foreach (var entity in model.Model.GetEntityTypes())
        {
            // The table name is set explicitly on every entity, so it is left
            // alone here; only owned types (the JSON columns) inherit a
            // generated one.
            foreach (var property in entity.GetProperties())
            {
                var storeObject = StoreObjectIdentifier.Create(entity, StoreObjectType.Table);

                if (storeObject is null)
                {
                    continue;
                }

                var current = property.GetColumnName(storeObject.Value);

                if (current is not null)
                {
                    property.SetColumnName(ToSnakeCase(current));
                }
            }
        }
    }

    /// <summary>
    /// Stores enums as text rather than as integers.
    /// </summary>
    /// <remarks>
    /// Costs a few bytes a row and buys two things. The database is readable
    /// without the C# to hand — <c>wellbeing = 'Concerned'</c> rather than
    /// <c>wellbeing = 3</c>, which matters when a staff member is looking at a
    /// query result. And reordering or inserting an enum member cannot silently
    /// reinterpret every existing row, which for <c>AttendanceStatus</c> or
    /// <c>WellbeingRating</c> would corrupt the record rather than merely
    /// confuse a report.
    /// </remarks>
    public static void StoreEnumsAsText(ModelBuilder model)
    {
        foreach (var entity in model.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                var underlying = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;

                if (underlying.IsEnum)
                {
                    property.SetProviderClrType(typeof(string));
                    property.SetMaxLength(40);
                }
            }
        }
    }

    /// <summary>ReferenceCode → reference_code; Section18AIssuedAt → section18a_issued_at.</summary>
    private static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);

        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];

            if (char.IsUpper(current))
            {
                // An underscore goes in only at a real word boundary, so an
                // acronym stays whole: IpAddress → ip_address, not i_p_address.
                var startsWord = i > 0
                    && (!char.IsUpper(name[i - 1])
                        || (i + 1 < name.Length && char.IsLower(name[i + 1])));

                if (startsWord)
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(current));
            }
            else if (char.IsDigit(current) && i > 0 && !char.IsDigit(name[i - 1]))
            {
                builder.Append(current);
            }
            else
            {
                builder.Append(current);
            }
        }

        return builder.ToString();
    }
}
