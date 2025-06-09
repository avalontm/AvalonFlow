using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;

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
            if (parameters != null)
            {
                foreach (var param in parameters)
                    cmd.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
            }

            using var adapter = new MySqlDataAdapter(cmd);
            var result = new DataTable();
            adapter.Fill(result);
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
    }
}

