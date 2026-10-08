using System;

namespace TestLogger.DbEngine
{
    public class RawDataPoint
    {
        public double Timestamp { get; set; }
        public double Value1 { get; set; }
        public double Value2 { get; set; }
        public double Value3 { get; set; }
        public double Value4 { get; set; }

        public RawDataPoint() { }
    }

    public class TrendDataPoint
    {
        public double Timestamp { get; set; }
        public double MinValue { get; set; }
        public double MaxValue { get; set; }
        public double AvgValue { get; set; }

        public TrendDataPoint() { }
    }

    public class TrendDisplayData
    {
        public double[] Timestamps { get; set; }
        public double[] MinValues { get; set; }
        public double[] MaxValues { get; set; }
        public double[] AvgValues { get; set; }

        public TrendDisplayData() { }
    }

    public class TestMetadata
    {
        public string TestName { get; set; }
        public string Operator { get; set; }
        public DateTime StartTime { get; set; }

        public TestMetadata() { }
    }
}
