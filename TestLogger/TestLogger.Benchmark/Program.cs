using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TestLogger.DbEngine;

namespace TestLogger.Benchmark
{
    class Program
    {
        static async Task Main(string[] args)
        {
            string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "testdb.sqlite");
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }

            Console.WriteLine("Starting Benchmark for SQLite High-Frequency Logger");
            Console.WriteLine($"Database Path: {dbPath}");

            CancellationTokenSource cts = new CancellationTokenSource();

            using (var dbEngineWrite = new DatabaseEngine())
            using (var dbEngineRead = new DatabaseEngine())
            {
                // Init with write engine
                dbEngineWrite.OpenDatabase(dbPath);
                dbEngineWrite.LogEvent("Benchmark Started");

                // Open second connection for read
                dbEngineRead.OpenDatabase(dbPath);

                // Start Background Write Task (10Hz)
                var writeTask = Task.Run(() => RunBackgroundWrites(dbEngineWrite, cts.Token));

                // Start Foreground Query Task (every 500ms)
                var readTask = Task.Run(() => RunForegroundReads(dbEngineRead, cts.Token));

                // Run for 10 seconds
                Console.WriteLine("Running for 10 seconds...");
                await Task.Delay(10000);

                cts.Cancel();

                await Task.WhenAll(writeTask, readTask);

                Console.WriteLine("\nBenchmark Finished.");
                Console.WriteLine(dbEngineRead.GetDatabaseStatus());
                dbEngineWrite.LogEvent("Benchmark Finished");
            }
        }

        static async Task RunBackgroundWrites(DatabaseEngine dbEngine, CancellationToken token)
        {
            long iteration = 0;
            var random = new Random();

            while (!token.IsCancellationRequested)
            {
                var rawData = new List<RawDataPoint>();
                var trendData = new List<TrendDataPoint>();

                // Simulate batch generation
                double timestamp = DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

                for(int i = 0; i < 100; i++)
                {
                    rawData.Add(new RawDataPoint
                    {
                        Timestamp = timestamp + (i * 0.001),
                        Value1 = random.NextDouble() * 100,
                        Value2 = random.NextDouble() * 100,
                        Value3 = random.NextDouble() * 100,
                        Value4 = random.NextDouble() * 100
                    });
                }

                trendData.Add(new TrendDataPoint
                {
                    Timestamp = timestamp,
                    MinValue = random.NextDouble() * 10,
                    MaxValue = random.NextDouble() * 90 + 10,
                    AvgValue = random.NextDouble() * 50 + 10
                });

                try
                {
                    dbEngine.WriteRawBatch(rawData);
                    dbEngine.WriteTrendBatch(trendData);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Write Error: {ex.Message}");
                }

                iteration++;

                // 10Hz = 100ms
                await Task.Delay(100, token).ContinueWith(t => { });
            }
        }

        static async Task RunForegroundReads(DatabaseEngine dbEngine, CancellationToken token)
        {
            Stopwatch sw = new Stopwatch();

            while (!token.IsCancellationRequested)
            {
                try
                {
                    sw.Restart();
                    var trendData = dbEngine.GetGlobalTrend3000();
                    var rawData = dbEngine.GetRecentRawRows(20);
                    sw.Stop();

                    if (sw.ElapsedMilliseconds > 100)
                    {
                        Console.WriteLine($"[WARNING] Query Time exceeded 100ms: {sw.ElapsedMilliseconds}ms");
                    }
                    else
                    {
                        Console.WriteLine($"[OK] Query Time: {sw.ElapsedMilliseconds}ms (Trend Length: {(trendData.Timestamps?.Length ?? 0)}, Raw Length: {rawData.Count})");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Read Error: {ex.Message}");
                }

                // 500ms
                await Task.Delay(500, token).ContinueWith(t => { });
            }
        }
    }
}
