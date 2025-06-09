using System.Data;

namespace AvalonFlow
{
    public static class DataRowExtensions
    {
        public static string GetString(this DataRow row, string columnName)
        {
            return row.Table.Columns.Contains(columnName) && row[columnName] != DBNull.Value
                ? row[columnName].ToString()!
                : string.Empty;
        }

        public static int GetInt(this DataRow row, string columnName)
        {
            return row.Table.Columns.Contains(columnName) && row[columnName] != DBNull.Value
                ? Convert.ToInt32(row[columnName])
                : 0;
        }

        public static long GetLong(this DataRow row, string columnName)
        {
            return row.Table.Columns.Contains(columnName) && row[columnName] != DBNull.Value
                ? Convert.ToInt64(row[columnName])
                : 0L;
        }

        public static bool GetBool(this DataRow row, string columnName)
        {
            return row.Table.Columns.Contains(columnName) && row[columnName] != DBNull.Value
                ? Convert.ToBoolean(row[columnName])
                : false;
        }

        public static double GetDouble(this DataRow row, string columnName)
        {
            return row.Table.Columns.Contains(columnName) && row[columnName] != DBNull.Value
                ? Convert.ToDouble(row[columnName])
                : 0.0;
        }

        public static float GetFloat(this DataRow row, string columnName)
        {
            return row.Table.Columns.Contains(columnName) && row[columnName] != DBNull.Value
                ? Convert.ToSingle(row[columnName])
                : 0f;
        }

        public static decimal GetDecimal(this DataRow row, string columnName)
        {
            return row.Table.Columns.Contains(columnName) && row[columnName] != DBNull.Value
                ? Convert.ToDecimal(row[columnName])
                : 0m;
        }

        public static DateTime GetDateTime(this DataRow row, string columnName)
        {
            return row.Table.Columns.Contains(columnName) && row[columnName] != DBNull.Value
                ? Convert.ToDateTime(row[columnName])
                : DateTime.MinValue;
        }

        public static Guid GetGuid(this DataRow row, string columnName)
        {
            return row.Table.Columns.Contains(columnName) && row[columnName] != DBNull.Value
                ? Guid.Parse(row[columnName].ToString()!)
                : Guid.Empty;
        }
    }
}
