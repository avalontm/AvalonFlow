using MySql.Data.MySqlClient;
using System.Data;

namespace AvalonFlow.MySql
{
    public class AvalonFlowSql
    {
        public static string? server { get; set; } = "localhost"; // Servidor por defecto de MySQL
        public static uint port { get; set; } = 3306;  // Puerto por defecto de MySQL
        public static string? user { get; set; } = "root"; // Usuario por defecto de MySQL
        public static string? password { get; set; } = ""; // Contraseña por defecto de MySQL
        public static string? database { get; set; } = "avalonflow"; // Base de datos por defecto de MySQL
        public static SslModeEnum sslmode { get; set; } = SslModeEnum.None;

        private readonly string _connectionString;

        public AvalonFlowSql(string server, uint port, string database, string user, string password, SslModeEnum sslmode = SslModeEnum.None)
        {
            _connectionString = $"Server={server};Port={port};Database={database};User ID={user};Password={password};SslMode={sslmode};Convert Zero Datetime=True;UseCompression=True;CharSet=utf8";
        }

        public AvalonFlowSql(string database)
        {
            if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(user))
                throw new InvalidOperationException("Las propiedades estáticas server, user y password deben estar definidas antes de usar este constructor.");

            _connectionString = $"Server={server};Port={port};Database={database};User ID={user};Password={password};SslMode={sslmode};Convert Zero Datetime=True;UseCompression=True;CharSet=utf8";
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
                // Aquí puedes loggear el error o manejarlo como quieras
                Console.WriteLine($"Error conectando a MySQL: {ex.Message}");
                return false;
            }
        }

        public DataTable ExecuteQuery(string query, Dictionary<string, object> parameters = null)
        {
            using var conn = new MySqlConnection(_connectionString);
            using var cmd = new MySqlCommand(query, conn);
            if (parameters != null)
            {
                foreach (var param in parameters)
                    cmd.Parameters.AddWithValue(param.Key, param.Value);
            }

            using var adapter = new MySqlDataAdapter(cmd);
            var result = new DataTable();
            adapter.Fill(result);
            return result;
        }

        public int ExecuteNonQuery(string query, Dictionary<string, object> parameters = null)
        {
            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(query, conn);
            if (parameters != null)
            {
                foreach (var param in parameters)
                    cmd.Parameters.AddWithValue(param.Key, param.Value);
            }
            return cmd.ExecuteNonQuery();
        }

        public object ExecuteScalar(string query, Dictionary<string, object> parameters = null)
        {
            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            using var cmd = new MySqlCommand(query, conn);
            if (parameters != null)
            {
                foreach (var param in parameters)
                    cmd.Parameters.AddWithValue(param.Key, param.Value);
            }
            return cmd.ExecuteScalar();
        }

        public List<DataTable> ExecuteMultipleQueries(List<string> queries)
        {
            var results = new List<DataTable>();
            using var conn = new MySqlConnection(_connectionString);
            conn.Open();

            foreach (var query in queries)
            {
                using var cmd = new MySqlCommand(query, conn);
                using var adapter = new MySqlDataAdapter(cmd);
                var dt = new DataTable();
                adapter.Fill(dt);
                results.Add(dt);
            }

            return results;
        }
    }
}