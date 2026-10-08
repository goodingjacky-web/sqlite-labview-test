using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;

namespace TestLogger.DbEngine
{
    public class DatabaseEngine : IDisposable
    {
        private SQLiteConnection _connection;

        public void OpenDatabase(string filePath)
        {
            bool isNew = !File.Exists(filePath);

            // Allow multiple connections (pooling), set BusyTimeout to handle locks in WAL mode
            string connectionString = $"Data Source={filePath};Version=3;Pooling=True;Max Pool Size=100;Journal Mode=WAL;BusyTimeout=5000;";
            _connection = new SQLiteConnection(connectionString);
            _connection.Open();

            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA journal_mode=WAL;";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "PRAGMA synchronous=NORMAL;";
                cmd.ExecuteNonQuery();

                if (isNew)
                {
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS RawData (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            Timestamp REAL,
                            Value1 REAL,
                            Value2 REAL,
                            Value3 REAL,
                            Value4 REAL
                        );
                        CREATE INDEX IF NOT EXISTS IDX_RawData_Timestamp ON RawData(Timestamp);

                        CREATE TABLE IF NOT EXISTS TrendData (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            Timestamp REAL,
                            MinValue REAL,
                            MaxValue REAL,
                            AvgValue REAL
                        );
                        CREATE INDEX IF NOT EXISTS IDX_TrendData_Timestamp ON TrendData(Timestamp);

                        CREATE TABLE IF NOT EXISTS Events (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
                            EventMessage TEXT
                        );
                    ";
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void CloseDatabase()
        {
            if (_connection != null)
            {
                if (_connection.State == System.Data.ConnectionState.Open)
                {
                    _connection.Close();
                }
                _connection.Dispose();
                _connection = null;
            }
        }

        public void WriteRawBatch(List<RawDataPoint> data)
        {
            if (data == null || data.Count == 0) return;

            using (var transaction = _connection.BeginTransaction())
            using (var cmd = _connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "INSERT INTO RawData (Timestamp, Value1, Value2, Value3, Value4) VALUES (@ts, @v1, @v2, @v3, @v4)";

                var pTs = cmd.Parameters.Add("@ts", System.Data.DbType.Double);
                var pV1 = cmd.Parameters.Add("@v1", System.Data.DbType.Double);
                var pV2 = cmd.Parameters.Add("@v2", System.Data.DbType.Double);
                var pV3 = cmd.Parameters.Add("@v3", System.Data.DbType.Double);
                var pV4 = cmd.Parameters.Add("@v4", System.Data.DbType.Double);

                foreach (var pt in data)
                {
                    pTs.Value = pt.Timestamp;
                    pV1.Value = pt.Value1;
                    pV2.Value = pt.Value2;
                    pV3.Value = pt.Value3;
                    pV4.Value = pt.Value4;
                    cmd.ExecuteNonQuery();
                }

                transaction.Commit();
            }
        }

        public void WriteTrendBatch(List<TrendDataPoint> data)
        {
            if (data == null || data.Count == 0) return;

            using (var transaction = _connection.BeginTransaction())
            using (var cmd = _connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "INSERT INTO TrendData (Timestamp, MinValue, MaxValue, AvgValue) VALUES (@ts, @min, @max, @avg)";

                var pTs = cmd.Parameters.Add("@ts", System.Data.DbType.Double);
                var pMin = cmd.Parameters.Add("@min", System.Data.DbType.Double);
                var pMax = cmd.Parameters.Add("@max", System.Data.DbType.Double);
                var pAvg = cmd.Parameters.Add("@avg", System.Data.DbType.Double);

                foreach (var pt in data)
                {
                    pTs.Value = pt.Timestamp;
                    pMin.Value = pt.MinValue;
                    pMax.Value = pt.MaxValue;
                    pAvg.Value = pt.AvgValue;
                    cmd.ExecuteNonQuery();
                }

                transaction.Commit();
            }
        }

        public void LogEvent(string message)
        {
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO Events (EventMessage) VALUES (@msg)";
                cmd.Parameters.AddWithValue("@msg", message);
                cmd.ExecuteNonQuery();
            }
        }

        public TrendDisplayData GetGlobalTrend3000()
        {
            var result = new TrendDisplayData();
            var timestamps = new List<double>();
            var minValues = new List<double>();
            var maxValues = new List<double>();
            var avgValues = new List<double>();

            using (var cmd = _connection.CreateCommand())
            {
                // Getting 3000 points evenly distributed, simplified by fetching the latest 3000
                cmd.CommandText = "SELECT Timestamp, MinValue, MaxValue, AvgValue FROM TrendData ORDER BY Timestamp DESC LIMIT 3000";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        timestamps.Add(reader.GetDouble(0));
                        minValues.Add(reader.GetDouble(1));
                        maxValues.Add(reader.GetDouble(2));
                        avgValues.Add(reader.GetDouble(3));
                    }
                }
            }

            // Since we ordered by DESC to get latest, reverse them to be in chronological order
            timestamps.Reverse();
            minValues.Reverse();
            maxValues.Reverse();
            avgValues.Reverse();

            result.Timestamps = timestamps.ToArray();
            result.MinValues = minValues.ToArray();
            result.MaxValues = maxValues.ToArray();
            result.AvgValues = avgValues.ToArray();

            return result;
        }

        public List<RawDataPoint> GetRecentRawRows(int count = 20)
        {
            var result = new List<RawDataPoint>();
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = $"SELECT Timestamp, Value1, Value2, Value3, Value4 FROM RawData ORDER BY Timestamp DESC LIMIT {count}";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        result.Add(new RawDataPoint
                        {
                            Timestamp = reader.GetDouble(0),
                            Value1 = reader.GetDouble(1),
                            Value2 = reader.GetDouble(2),
                            Value3 = reader.GetDouble(3),
                            Value4 = reader.GetDouble(4)
                        });
                    }
                }
            }
            result.Reverse();
            return result;
        }

        public string GetDatabaseStatus()
        {
            if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
            {
                return "Database is not open.";
            }

            long rawCount = 0;
            long trendCount = 0;

            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM RawData";
                rawCount = (long)cmd.ExecuteScalar();

                cmd.CommandText = "SELECT COUNT(*) FROM TrendData";
                trendCount = (long)cmd.ExecuteScalar();
            }

            return $"RawData: {rawCount} rows, TrendData: {trendCount} rows";
        }

        public void Dispose()
        {
            CloseDatabase();
        }
    }
}
