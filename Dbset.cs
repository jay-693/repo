using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Data.SqlClient;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace orm1
{
    public static class ORM
    {
        private static string _connectionString;

        public static void Initialize(string connectionString)
        {
            _connectionString = connectionString;
            SyncDatabase();
        }

        private static void SyncDatabase()
        {
            var models = Assembly.GetExecutingAssembly().GetTypes()
                .Where(t => t.GetCustomAttribute<TableAttribute>() != null);

            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();

                // Drop tables for models that no longer exist
                var existingTables = GetExistingTables(connection);
                foreach (var table in existingTables)
                {
                    if (!models.Any(m => m.GetCustomAttribute<TableAttribute>()?.Name == table))
                    {
                        var dropTableQuery = $"DROP TABLE [{table}];";
                        using (var command = new SqlCommand(dropTableQuery, connection))
                        {
                            command.ExecuteNonQuery();
                        }
                    }
                }

                // Sync models to database
                foreach (var model in models)
                {
                    var tableName = model.GetCustomAttribute<TableAttribute>().Name;
                    var properties = model.GetProperties();

                    var createTableQuery = GenerateCreateTableQuery(tableName, properties);
                    using (var command = new SqlCommand(createTableQuery, connection))
                    {
                        command.ExecuteNonQuery();
                    }

                    // Update primary key if it changes
                    SyncPrimaryKey(tableName, properties, connection);

                    // Update foreign keys if they exist
                    SyncForeignKeys(tableName, properties, connection);

                    // Alter table columns if necessary
                    SyncColumns(tableName, properties, connection);
                }
            }
        }

        private static void SyncPrimaryKey(string tableName, PropertyInfo[] properties, SqlConnection connection)
        {
            var primaryKeyProperty = properties.FirstOrDefault(p => p.GetCustomAttribute<KeyAttribute>() != null);

            // Check if a primary key exists in the model
            if (primaryKeyProperty != null)
            {
                // Check if the primary key constraint already exists in the database
                var columnName = primaryKeyProperty.Name;
                var checkPrimaryKeyQuery = $@"
            IF NOT EXISTS (
                SELECT * FROM sys.indexes 
                WHERE object_id = OBJECT_ID('{tableName}') 
                AND is_primary_key = 1
            )
            ALTER TABLE [{tableName}] ADD CONSTRAINT PK_{tableName} PRIMARY KEY CLUSTERED([{columnName}]);
        ";
                using (var command = new SqlCommand(checkPrimaryKeyQuery, connection))
                {
                    command.ExecuteNonQuery();
                }
            }
            else
            {
                // If there's no primary key property, drop the existing primary key constraint
                var dropPrimaryKeyQuery = $@"
            IF EXISTS (
                SELECT * FROM sys.indexes 
                WHERE object_id = OBJECT_ID('{tableName}') 
                AND is_primary_key = 1
            )
            ALTER TABLE [{tableName}] DROP CONSTRAINT PK_{tableName};
        ";
                using (var command = new SqlCommand(dropPrimaryKeyQuery, connection))
                {
                    command.ExecuteNonQuery();
                }
            }
        }

        private static void SyncForeignKeys(string tableName, PropertyInfo[] properties, SqlConnection connection)
        {
            foreach (var property in properties)
            {
                var foreignKeyAttr = property.GetCustomAttribute<ForeignKeyAttribute>();
                if (foreignKeyAttr != null)
                {
                    // Get the foreign key column and referenced table
                    var foreignKeyColumn = property.Name;
                    var referencedType = property.PropertyType;

                    // Find the primary key property in the referenced type
                    var referencedPrimaryKey = referencedType.GetProperties()
                        .FirstOrDefault(p => p.GetCustomAttribute<KeyAttribute>() != null);

                    if (referencedPrimaryKey == null)
                    {
                        throw new InvalidOperationException($"Referenced type {referencedType.Name} does not have a primary key.");
                    }

                    var referencedTable = referencedType.GetCustomAttribute<TableAttribute>()?.Name
                        ?? throw new InvalidOperationException($"Referenced type {referencedType.Name} does not have a Table attribute.");

                    var referencedColumn = referencedPrimaryKey.Name;

                    // SQL to add the foreign key
                    var addForeignKeyQuery = $@"
                IF NOT EXISTS (
                    SELECT * 
                    FROM sys.foreign_keys 
                    WHERE parent_object_id = OBJECT_ID('{tableName}') 
                    AND name = 'FK_{tableName}_{foreignKeyColumn}'
                )
                ALTER TABLE [{tableName}] 
                ADD CONSTRAINT FK_{tableName}_{foreignKeyColumn} 
                FOREIGN KEY([{foreignKeyColumn}]) REFERENCES [{referencedTable}]([{referencedColumn}]);
            ";

                    using (var command = new SqlCommand(addForeignKeyQuery, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
            }
        }

        private static List<string> GetExistingTables(SqlConnection connection)
        {
            var tables = new List<string>();
            var query = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE';";

            using (var command = new SqlCommand(query, connection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    tables.Add(reader.GetString(0));
                }
            }

            return tables;
        }
        private static void SyncColumns(string tableName, PropertyInfo[] properties, SqlConnection connection)
        {
            var existingColumns = GetExistingColumns(tableName, connection);
            var propertyNames = properties.Select(p => p.Name).ToList();

            // Drop columns that no longer exist in the model
            foreach (var column in existingColumns)
            {
                if (!propertyNames.Contains(column))
                {
                    var dropColumnQuery = $"ALTER TABLE [{tableName}] DROP COLUMN [{column}];";
                    using (var command = new SqlCommand(dropColumnQuery, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
            }

            // Add new columns
            foreach (var prop in properties)
            {
                if (!existingColumns.Contains(prop.Name))
                {
                    var addColumnQuery = $"ALTER TABLE [{tableName}] ADD [{prop.Name}] {GetSqlType(prop.PropertyType)};";
                    using (var command = new SqlCommand(addColumnQuery, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
            }
        }
        private static List<string> GetExistingColumns(string tableName, SqlConnection connection)
        {
            var columns = new List<string>();
            var query = $"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{tableName}';";

            using (var command = new SqlCommand(query, connection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    columns.Add(reader.GetString(0));
                }
            }

            return columns;
        }
        private static string GenerateCreateTableQuery(string tableName, PropertyInfo[] properties)
        {
            var columns = new List<string>();

            foreach (var prop in properties)
            {
                var columnName = prop.Name;
                var columnType = GetSqlType(prop.PropertyType);
                var isPrimaryKey = prop.GetCustomAttribute<KeyAttribute>() != null;

                var columnDefinition = isPrimaryKey
                    ? $"[{columnName}] {columnType} PRIMARY KEY IDENTITY(1,1)"
                    : $"[{columnName}] {columnType}";

                columns.Add(columnDefinition);
            }

            return $"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='{tableName}' AND xtype='U') CREATE TABLE [{tableName}] ({string.Join(", ", columns)});";
        }

        private static string GetSqlType(Type type)
        {
            // Check for nullable types and extract the underlying type
            var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

            return underlyingType switch
            {
                _ when underlyingType == typeof(int) => "INT",
                _ when underlyingType == typeof(long) => "BIGINT",
                _ when underlyingType == typeof(decimal) => "DECIMAL(18,2)",
                _ when underlyingType == typeof(float) => "FLOAT",
                _ when underlyingType == typeof(double) => "DOUBLE PRECISION",
                _ when underlyingType == typeof(string) => "NVARCHAR(MAX)",
                _ when underlyingType == typeof(bool) => "BIT",
                _ when underlyingType == typeof(DateTime) => "DATETIME",
                _ when underlyingType == typeof(Guid) => "UNIQUEIDENTIFIER",
                _ when underlyingType == typeof(byte[]) => "VARBINARY(MAX)",
                _ when underlyingType == typeof(short) => "SMALLINT",
                _ when underlyingType == typeof(byte) => "TINYINT",
                _ when underlyingType == typeof(char) => "NCHAR(1)",
                _ => throw new NotSupportedException($"Type {underlyingType} is not supported")
            };
        }
        private static PropertyInfo[] GetDatabaseProperties(Type modelType)
        {
            return modelType.GetProperties()
                .Where(p =>
                    // Include only properties that are primitive or nullable types
                    (p.PropertyType.IsPrimitive || // Primitive types
                     p.PropertyType == typeof(string) || // Strings
                     p.PropertyType == typeof(DateTime) || // DateTimes
                     p.PropertyType == typeof(decimal) || // Decimal
                     p.PropertyType == typeof(Guid) || // Guid
                     p.PropertyType == typeof(byte[]) || // Byte arrays
                     Nullable.GetUnderlyingType(p.PropertyType) != null) // Nullable types
                )
                .ToArray();
        }



        public static void Insert<T>(T entity) where T : class
        {
            var tableName = typeof(T).GetCustomAttribute<TableAttribute>().Name;

            // Exclude identity column (marked with [Key]) from insertion
            var properties = typeof(T).GetProperties().Where(p => p.GetCustomAttribute<KeyAttribute>() == null);

            var columnNames = string.Join(", ", properties.Select(p => p.Name));
            var columnValues = string.Join(", ", properties.Select(p => $"'{p.GetValue(entity)?.ToString().Replace("'", "''")}'"));

            var query = $"INSERT INTO [{tableName}] ({columnNames}) VALUES ({columnValues});";

            ExecuteNonQuery(query);
        }

        public static List<T> GetAll<T>() where T : class, new()
        {
            var tableName = typeof(T).GetCustomAttribute<TableAttribute>().Name;
            var query = $"SELECT * FROM [{tableName}];";

            var result = new List<T>();
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (var command = new SqlCommand(query, connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var entity = new T();
                        foreach (var prop in typeof(T).GetProperties())
                        {
                            var columnName = prop.Name;
                            var value = reader[columnName];

                            if (value != DBNull.Value) // Check for DBNull
                            {
                                prop.SetValue(entity, Convert.ChangeType(value, Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType));
                            }
                        }
                        result.Add(entity);
                    }
                }
            }
            return result;
        }


        public static void Update<T>(T entity) where T : class
        {
            var tableName = typeof(T).GetCustomAttribute<TableAttribute>().Name;
            var keyProperty = typeof(T).GetProperties().FirstOrDefault(p => p.GetCustomAttribute<KeyAttribute>() != null);
            var properties = typeof(T).GetProperties().Where(p => p != keyProperty);

            var setClause = string.Join(", ", properties.Select(p => $"[{p.Name}] = '" + p.GetValue(entity)?.ToString().Replace("'", "''") + "'"));
            var keyClause = $"[{keyProperty.Name}] = {keyProperty.GetValue(entity)}";

            var query = $"UPDATE [{tableName}] SET {setClause} WHERE {keyClause};";

            ExecuteNonQuery(query);
        }

        public static void Delete<T>(T entity) where T : class
        {
            var tableName = typeof(T).GetCustomAttribute<TableAttribute>().Name;
            var keyProperty = typeof(T).GetProperties().First(p => p.GetCustomAttribute<KeyAttribute>() != null);
            var keyClause = $"[{keyProperty.Name}] = {keyProperty.GetValue(entity)}";

            var query = $"DELETE FROM [{tableName}] WHERE {keyClause};";

            ExecuteNonQuery(query);
        }

        private static void ExecuteNonQuery(string query)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (var command = new SqlCommand(query, connection))
                {
                    command.ExecuteNonQuery();
                }
            }
        }
    }
}
