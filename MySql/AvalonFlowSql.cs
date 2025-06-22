using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using System.Text;

namespace AvalonFlow.MySql
{
    public class AvalonFlowSql : IDisposable
    {
        public static string? server { get; set; } = "localhost";
        public static uint port { get; set; } = 3306;
        public static string? user { get; set; } = "root";
        public static string? password { get; set; } = "";
        public static string? database { get; set; } = "avalonflow";
        public static SslModeEnum sslmode { get; set; } = SslModeEnum.None;

        private readonly string _connectionString;
        private readonly MySqlConnection _connection;

        // Constructor que abre la conexión y la mantiene abierta durante la vida del objeto
        public AvalonFlowSql(string database)
        {
            if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(user))
                throw new InvalidOperationException("Las propiedades estáticas server, user y password deben estar definidas antes de usar este constructor.");

            _connectionString = $"Server={server};Port={port};Database={database};User ID={user};Password={password};SslMode={sslmode};Convert Zero Datetime=True;UseCompression=True;CharSet=utf8";
            _connection = new MySqlConnection(_connectionString);
            _connection.Open();
        }

        public static void Configure(
    string server,
    string user,
    string password,
    uint port = 3306,
    SslModeEnum sslmode = SslModeEnum.None)
        {
            AvalonFlowSql.server = server;
            AvalonFlowSql.user = user;
            AvalonFlowSql.password = password;
            AvalonFlowSql.port = port;
            AvalonFlowSql.sslmode = sslmode;
        }


        public bool TestConnection()
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                connection.Open();
                connection.Close();
                return true;
            }
            catch (MySqlException ex)
            {
                Console.WriteLine($"Error conectando a MySQL: {ex.Message}");
                return false;
            }
        }

        // Método para cerrar la conexión y liberar recursos
        public void Dispose()
        {
            if (_connection != null)
            {
                if (_connection.State == ConnectionState.Open)
                    _connection.Close();

                _connection.Dispose();
            }
            GC.SuppressFinalize(this);
        }

        public DataTable ExecuteQuery(string query, Dictionary<string, object>? parameters = null)
        {
            using var cmd = new MySqlCommand(query, _connection);

            // Asignar parámetros
            if (parameters != null)
            {
                foreach (var param in parameters)
                {
                    string paramName = param.Key.StartsWith("@") ? param.Key : "@" + param.Key;
                    cmd.Parameters.AddWithValue(paramName, param.Value ?? DBNull.Value);
                }
            }

            var result = new DataTable();

            try
            {
                if (_connection.State != ConnectionState.Open)
                    _connection.Open();

                using var adapter = new MySqlDataAdapter(cmd);
                adapter.Fill(result);
            }
            finally
            {
                if (_connection.State == ConnectionState.Open)
                    _connection.Close();
            }

            return result;
        }

        public int ExecuteNonQuery(string query, Dictionary<string, object>? parameters = null)
        {
            using var cmd = new MySqlCommand(query, _connection);
            if (parameters != null)
            {
                foreach (var param in parameters)
                    cmd.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
            }
            return cmd.ExecuteNonQuery();
        }

        public object? ExecuteScalar(string query, Dictionary<string, object>? parameters = null)
        {
            using var cmd = new MySqlCommand(query, _connection);
            if (parameters != null)
            {
                foreach (var param in parameters)
                    cmd.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
            }
            return cmd.ExecuteScalar();
        }

        public List<DataTable> ExecuteMultipleQueries(List<string> queries)
        {
            var results = new List<DataTable>();
            foreach (var query in queries)
            {
                using var cmd = new MySqlCommand(query, _connection);
                using var adapter = new MySqlDataAdapter(cmd);
                var dt = new DataTable();
                adapter.Fill(dt);
                results.Add(dt);
            }
            return results;
        }

        public static bool InstallDatabase(string databaseName, string sqlFilePath, out string errorMessage)
        {
            errorMessage = string.Empty;

            try
            {
                // Validaciones iniciales
                if (string.IsNullOrWhiteSpace(databaseName))
                {
                    errorMessage = "El nombre de la base de datos no puede estar vacío";
                    return false;
                }

                if (!File.Exists(sqlFilePath))
                {
                    errorMessage = $"Archivo SQL no encontrado: {sqlFilePath}";
                    return false;
                }

                // 1. Conexión maestra para crear la base de datos
                string masterConnectionString = $"Server={server};Port={port};User ID={user};Password={password};SslMode={sslmode};ConnectionTimeout=30";

                using (var connection = new MySqlConnection(masterConnectionString))
                {
                    connection.Open();

                    // 1.1. Verificar si la base de datos ya existe
                    bool dbExists = false;
                    using (var checkCmd = new MySqlCommand($"SELECT SCHEMA_NAME FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = '{databaseName}';", connection))
                    {
                        dbExists = checkCmd.ExecuteScalar() != null;
                    }

                    // 1.2. Crear la base de datos si no existe
                    if (!dbExists)
                    {
                        using (var createCmd = new MySqlCommand($"CREATE DATABASE `{databaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;", connection))
                        {
                            if (createCmd.ExecuteNonQuery() == -1)
                            {
                                Console.WriteLine($"Base de datos '{databaseName}' creada exitosamente");
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Base de datos '{databaseName}' ya existe");
                    }

                    // 1.3. Otorgar permisos (con verificación)
                    using (var grantCmd = new MySqlCommand($"GRANT ALL PRIVILEGES ON `{databaseName}`.* TO '{user}'@'%'; FLUSH PRIVILEGES;", connection))
                    {
                        grantCmd.ExecuteNonQuery();
                        Console.WriteLine("Permisos configurados correctamente");
                    }
                }
                // 2. Leer el contenido del archivo SQL
                string sqlScript = File.ReadAllText(sqlFilePath, Encoding.UTF8);

                // 3. Configurar conexión con tiempo de espera extendido
                string connectionString = $"Server={server};Port={port};Database={databaseName};User ID={user};Password={password};SslMode={sslmode};AllowLoadLocalInfile=true;ConnectionTimeout=120;DefaultCommandTimeout=600";

                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();

                    // 4. Crear un objeto MySqlScript para ejecutar el script completo
                    var script = new MySqlScript(connection, sqlScript);

                    script.Delimiter = ";"; // Establecer el delimitador

                    // Mover el manejo de errores fuera del delegado para evitar el uso de "out"
                    bool hasError = false;
                    string localErrorMessage = string.Empty;

                    script.Error += (sender, args) =>
                    {
                        localErrorMessage = $"Error en script SQL: {args.Exception.Message}";
                        hasError = true;
                        args.Ignore = false; // No ignorar errores
                    };

                    // 5. Ejecutar el script
                    int count = script.Execute();

                    if (hasError)
                    {
                        errorMessage = localErrorMessage;
                        return false;
                    }

                    Console.WriteLine($"Script ejecutado correctamente. {count} comandos procesados.");
                    return true;
                }
            }
            catch (MySqlException ex)
            {
                errorMessage = $"Error MySQL ({ex.Number}): {ex.Message}";
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = $"Error inesperado: {ex.Message}";
                return false;
            }
        }

    }
}