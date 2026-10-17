namespace MicroORM.Tests.Integration;

/// <summary>DDL per provider, as separate statements (SQL Server needs CREATE PROCEDURE alone in its batch).</summary>
internal static class TestSchema
{
    private static readonly string[] Tables =
        ["micro_people", "micro_tags", "micro_orders", "micro_order_lines", "micro_documents", "micro_customers"];

    public static IEnumerable<string> For(string provider)
    {
        foreach (var table in Tables)
        {
            yield return $"DROP TABLE IF EXISTS {table}";
        }

        foreach (var statement in Create(provider))
        {
            yield return statement;
        }
    }

    private static string[] Create(string provider) => provider switch
    {
        "sqlite" =>
        [
            """
            CREATE TABLE micro_people (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, email TEXT NULL, age INTEGER NOT NULL,
                active INTEGER NOT NULL, mood INTEGER NOT NULL, created_at TEXT NOT NULL, external_id TEXT NULL)
            """,
            "CREATE TABLE micro_tags (code TEXT PRIMARY KEY, label TEXT NOT NULL)",
            "CREATE TABLE micro_orders (id INTEGER PRIMARY KEY AUTOINCREMENT, person_id INTEGER NULL, amount INTEGER NOT NULL)",
            "CREATE TABLE micro_order_lines (order_id INTEGER NOT NULL, line_no INTEGER NOT NULL, product TEXT NOT NULL, quantity INTEGER NOT NULL, PRIMARY KEY (order_id, line_no))",
            "CREATE TABLE micro_documents (id INTEGER PRIMARY KEY AUTOINCREMENT, title TEXT NOT NULL, version INTEGER NOT NULL)",
            "CREATE TABLE micro_customers (id INTEGER PRIMARY KEY AUTOINCREMENT, email TEXT NOT NULL UNIQUE, name TEXT NOT NULL, address TEXT NULL, tags TEXT NOT NULL, tier TEXT NOT NULL)",
        ],
        "postgres" =>
        [
            """
            CREATE TABLE micro_people (id SERIAL PRIMARY KEY, name TEXT NOT NULL, email TEXT NULL, age INT NOT NULL,
                active BOOLEAN NOT NULL, mood INT NOT NULL, created_at TIMESTAMPTZ NOT NULL, external_id UUID NULL)
            """,
            "CREATE TABLE micro_tags (code UUID PRIMARY KEY, label TEXT NOT NULL)",
            "CREATE TABLE micro_orders (id SERIAL PRIMARY KEY, person_id INT NULL, amount INT NOT NULL)",
            "CREATE TABLE micro_order_lines (order_id INT NOT NULL, line_no INT NOT NULL, product TEXT NOT NULL, quantity INT NOT NULL, PRIMARY KEY (order_id, line_no))",
            "CREATE TABLE micro_documents (id SERIAL PRIMARY KEY, title TEXT NOT NULL, version INT NOT NULL)",
            "CREATE TABLE micro_customers (id SERIAL PRIMARY KEY, email TEXT NOT NULL UNIQUE, name TEXT NOT NULL, address JSONB NULL, tags TEXT NOT NULL, tier TEXT NOT NULL)",
            "CREATE OR REPLACE PROCEDURE micro_add(a INT, b INT, OUT total INT) LANGUAGE plpgsql AS $$ BEGIN total := a + b; END $$",
        ],
        "sqlserver" =>
        [
            """
            CREATE TABLE micro_people (id INT IDENTITY PRIMARY KEY, name NVARCHAR(200) NOT NULL, email NVARCHAR(200) NULL, age INT NOT NULL,
                active BIT NOT NULL, mood INT NOT NULL, created_at DATETIME2 NOT NULL, external_id UNIQUEIDENTIFIER NULL)
            """,
            "CREATE TABLE micro_tags (code UNIQUEIDENTIFIER PRIMARY KEY, label NVARCHAR(200) NOT NULL)",
            "CREATE TABLE micro_orders (id INT IDENTITY PRIMARY KEY, person_id INT NULL, amount INT NOT NULL)",
            "CREATE TABLE micro_order_lines (order_id INT NOT NULL, line_no INT NOT NULL, product NVARCHAR(200) NOT NULL, quantity INT NOT NULL, PRIMARY KEY (order_id, line_no))",
            "CREATE TABLE micro_documents (id INT IDENTITY PRIMARY KEY, title NVARCHAR(200) NOT NULL, version INT NOT NULL)",
            "CREATE TABLE micro_customers (id INT IDENTITY PRIMARY KEY, email NVARCHAR(200) NOT NULL UNIQUE, name NVARCHAR(200) NOT NULL, address NVARCHAR(MAX) NULL, tags NVARCHAR(MAX) NOT NULL, tier NVARCHAR(20) NOT NULL)",
            "CREATE OR ALTER PROCEDURE micro_add @a INT, @b INT, @total INT OUTPUT AS BEGIN SET @total = @a + @b; RETURN 7; END",
            """
            CREATE OR ALTER PROCEDURE micro_people_older_than @min_age INT, @total_count INT OUTPUT AS
            BEGIN
                SET NOCOUNT ON;
                SELECT id, name, age FROM micro_people WHERE age > @min_age ORDER BY age;
                SELECT @total_count = COUNT(*) FROM micro_people WHERE age > @min_age;
            END
            """,
        ],
        "mysql" =>
        [
            """
            CREATE TABLE micro_people (id INT AUTO_INCREMENT PRIMARY KEY, name VARCHAR(200) NOT NULL, email VARCHAR(200) NULL, age INT NOT NULL,
                active BOOLEAN NOT NULL, mood INT NOT NULL, created_at DATETIME(6) NOT NULL, external_id CHAR(36) NULL)
            """,
            "CREATE TABLE micro_tags (code CHAR(36) PRIMARY KEY, label VARCHAR(200) NOT NULL)",
            "CREATE TABLE micro_orders (id INT AUTO_INCREMENT PRIMARY KEY, person_id INT NULL, amount INT NOT NULL)",
            "CREATE TABLE micro_order_lines (order_id INT NOT NULL, line_no INT NOT NULL, product VARCHAR(200) NOT NULL, quantity INT NOT NULL, PRIMARY KEY (order_id, line_no))",
            "CREATE TABLE micro_documents (id INT AUTO_INCREMENT PRIMARY KEY, title VARCHAR(200) NOT NULL, version INT NOT NULL)",
            "CREATE TABLE micro_customers (id INT AUTO_INCREMENT PRIMARY KEY, email VARCHAR(200) NOT NULL UNIQUE, name VARCHAR(200) NOT NULL, address JSON NULL, tags TEXT NOT NULL, tier VARCHAR(20) NOT NULL)",
            "DROP PROCEDURE IF EXISTS micro_add",
            "CREATE PROCEDURE micro_add(IN a INT, IN b INT, OUT total INT) BEGIN SET total = a + b; END",
            "DROP PROCEDURE IF EXISTS micro_people_older_than",
            """
            CREATE PROCEDURE micro_people_older_than(IN min_age INT, OUT total_count INT)
            BEGIN
                SELECT id, name, age FROM micro_people WHERE age > min_age ORDER BY age;
                SELECT COUNT(*) INTO total_count FROM micro_people WHERE age > min_age;
            END
            """,
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(provider)),
    };
}
