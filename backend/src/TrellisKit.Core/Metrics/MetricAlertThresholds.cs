namespace TrellisKit.Core.Metrics
{
    using System;
    /// <summary>Property-configurable defaults for the seven engine alert families.</summary>
    public sealed class MetricAlertThresholds
    {
        private double _ServerErrorPercent=2;
        private double _LatencyP95Ms=2000;
        private int _SustainedMinutes=10;
        private int _EndpointFailedChecks=3;
        private int _WorkerFailedPasses=2;
        private int _WorkerMissedIntervals=3;
        private int _RollupLagMinutes=120;
        private double _SignupRatio=.5;
        private int _StorageProjectionDays=30;
        /// <summary>Server error percentage, 0 to 100; default 2.</summary>
        public double ServerErrorPercent {get=>_ServerErrorPercent;set=>_ServerErrorPercent=Range(value,0,100);}
        /// <summary>p95 latency in milliseconds, 1 to 86400000; default 2000.</summary>
        public double LatencyP95Ms {get=>_LatencyP95Ms;set=>_LatencyP95Ms=Range(value,1,86400000);}
        /// <summary>Consecutive one-minute operational checks, 1 to 60; default 10.</summary>
        public int SustainedMinutes {get=>_SustainedMinutes;set=>_SustainedMinutes=(int)Range(value,1,60);}
        /// <summary>Failed endpoint health checks, 1 to 100; default 3.</summary>
        public int EndpointFailedChecks {get=>_EndpointFailedChecks;set=>_EndpointFailedChecks=(int)Range(value,1,100);}
        /// <summary>Consecutive failed worker passes, 1 to 100; default 2.</summary>
        public int WorkerFailedPasses {get=>_WorkerFailedPasses;set=>_WorkerFailedPasses=(int)Range(value,1,100);}
        /// <summary>Missed worker intervals before alert, 1 to 100; default 3.</summary>
        public int WorkerMissedIntervals {get=>_WorkerMissedIntervals;set=>_WorkerMissedIntervals=(int)Range(value,1,100);}
        /// <summary>Rollup watermark lag in minutes, 1 to 10080; default 120.</summary>
        public int RollupLagMinutes {get=>_RollupLagMinutes;set=>_RollupLagMinutes=(int)Range(value,1,10080);}
        /// <summary>Daily sign-ups as a fraction of the trailing 14-day average, 0 to 1; default .5.</summary>
        public double SignupRatio {get=>_SignupRatio;set=>_SignupRatio=Range(value,0,1);}
        /// <summary>Days projected ahead for a configured storage quota, 1 to 365; default 30.</summary>
        public int StorageProjectionDays {get=>_StorageProjectionDays;set=>_StorageProjectionDays=(int)Range(value,1,365);}
        private static double Range(double value,double min,double max) {if(!Double.IsFinite(value)||value<min||value>max)throw new ArgumentOutOfRangeException(nameof(value));return value;}
    }
}
