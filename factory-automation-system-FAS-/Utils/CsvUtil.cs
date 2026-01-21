using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;

namespace factory_automation_system_FAS_.Utils
{
    public static class CsvUtil
    {
        // Excel 호환성을 위해 UTF-8 BOM을 기본으로 씀
        public static void WriteDataTableToCsv(DataTable table, string filePath)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("Invalid file path", nameof(filePath));

            var sb = new StringBuilder();

            // Header
            var headers = table.Columns.Cast<DataColumn>().Select(c => Escape(c.ColumnName));
            sb.AppendLine(string.Join(",", headers));

            // Rows
            foreach (DataRow row in table.Rows)
            {
                var fields = table.Columns.Cast<DataColumn>()
                    .Select(c => Escape(row[c]?.ToString() ?? string.Empty));
                sb.AppendLine(string.Join(",", fields));
            }

            var utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            File.WriteAllText(filePath, sb.ToString(), utf8Bom);
        }

        private static string Escape(string s)
        {
            if (s == null) return "";
            bool mustQuote = s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r');
            if (s.Contains('"')) s = s.Replace("\"", "\"\"");
            return mustQuote ? $"\"{s}\"" : s;
        }
    }
}
