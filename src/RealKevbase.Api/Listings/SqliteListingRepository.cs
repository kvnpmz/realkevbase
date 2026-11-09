using Microsoft.Data.Sqlite;

using Microsoft.Extensions.Options;

namespace RealKevbase.Api.Listings;

/// <summary>
/// Options controlling where the listing database is read from.
/// </summary>
public sealed class ListingsOptions
{
    /// <summary>
    /// Configuration section that holds these options.
    /// </summary>
    public const string SectionName = "Listings";

    /// <summary>
    /// Gets or sets the path to the SQLite listing database. Relative paths are resolved
    /// against the application's content root.
    /// </summary>
    public string DatabasePath { get; set; } = "Data/projects.db";
}

/// <summary>
/// Read-only access to the listings that back the digital catalog.
/// </summary>
public interface IListingRepository
{
    /// <summary>
    /// Reads every listing from the listing database.
    /// </summary>
    /// <returns>
    /// All listings ordered by identifier, or <see langword="null"/> when the database is
    /// missing or unreadable. Callers must treat <see langword="null"/> as "listing data
    /// unavailable" rather than "no listings", so a storage problem cannot empty the storefront.
    /// </returns>
    IReadOnlyList<Listing>? GetAll();
}

/// <summary>
/// Reads listings from the SQLite database produced by the PDF generator.
/// </summary>
/// <remarks>
/// The database is opened read-only and is never written by the API: it is a build artifact of
/// the generator, shipped alongside the application. A missing or malformed file is logged and
/// reported as unavailable instead of failing startup.
/// </remarks>
public sealed class SqliteListingRepository : IListingRepository
{
    private readonly string _databasePath;
    private readonly string _connectionString;
    private readonly ILogger<SqliteListingRepository> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteListingRepository"/> class.
    /// </summary>
    /// <param name="options">Listing options carrying the database path.</param>
    /// <param name="environment">Host environment used to resolve a relative database path.</param>
    /// <param name="logger">Logger for read failures.</param>
    public SqliteListingRepository(
        IOptions<ListingsOptions> options,
        IHostEnvironment environment,
        ILogger<SqliteListingRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(logger);

        var configured = options.Value.DatabasePath;
        _databasePath = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(environment.ContentRootPath, configured);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();

        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<Listing>? GetAll()
    {
        if (!File.Exists(_databasePath))
        {
            _logger.LogWarning(
                "Listing database not found at {DatabasePath}; serving the catalog without listing metadata.",
                _databasePath);
            return null;
        }

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT id, output_name, template, completed_date, is_editable
                FROM Projects
                ORDER BY id
                """;

            using var reader = command.ExecuteReader();
            var listings = new List<Listing>();

            while (reader.Read())
            {
                listings.Add(new Listing
                {
                    Id = reader.GetString(0),
                    OutputName = reader.IsDBNull(1) ? null : reader.GetString(1),
                    Template = reader.IsDBNull(2) ? null : reader.GetString(2),
                    CompletedDate = reader.IsDBNull(3) ? null : reader.GetString(3),
                    IsEditable = !reader.IsDBNull(4) && reader.GetInt64(4) != 0
                });
            }

            return listings;
        }
        catch (SqliteException exception)
        {
            _logger.LogWarning(
                exception,
                "Could not read the listing database at {DatabasePath}; serving the catalog without listing metadata.",
                _databasePath);
            return null;
        }
    }
}

/// <summary>
/// Registration helpers for the listing repository.
/// </summary>
public static class ListingsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the listing repository and its options.
    /// </summary>
    /// <param name="services">The service collection to extend.</param>
    /// <param name="configuration">Application configuration containing the <c>Listings</c> section.</param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
    public static IServiceCollection AddListings(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<ListingsOptions>(configuration.GetSection(ListingsOptions.SectionName));
        services.AddSingleton<IListingRepository, SqliteListingRepository>();

        return services;
    }
}
