using System;
using System.Collections.Generic;
using System.Linq;

namespace Tharga.MongoDB.Compress;

public static class StrataHelper
{
    public static Strata GetStrata(IEnumerable<Strata> stratas, DateTime dateTime)
    {
        if (stratas == null) return null;

        var age = GetAge(dateTime);

        var strata = stratas
            .Where(x => age >= x.WhenOlderThan)
            .MaxBy(x => x.WhenOlderThan);

        return strata;
    }

    public static CompressGranularity GetAge(DateTime? time)
    {
        if (time == null) return CompressGranularity.None;

        var age = DateTime.UtcNow - time.Value;
        CompressGranularity granularity;
        if (age.TotalDays > 365)
            granularity = CompressGranularity.Year;
        else if (age.TotalDays > 90)
            granularity = CompressGranularity.Quarter;
        else if (age.TotalDays > 30)
            granularity = CompressGranularity.Month;
        else if (age.TotalDays > 7)
            granularity = CompressGranularity.Week;
        else if (age.TotalDays > 1)
            granularity = CompressGranularity.Day;
        else if (age.TotalHours > 1)
            granularity = CompressGranularity.Hour;
        else if (age.TotalMinutes > 1)
            granularity = CompressGranularity.Minute;
        else
            granularity = CompressGranularity.None;

        return granularity;
    }

    public static TimeSpan GetTimeSpan(CompressGranularity strataGranularity)
    {
        switch (strataGranularity)
        {
            case CompressGranularity.None:
                return TimeSpan.Zero;
                //throw new NotSupportedException($"Getting time span from '{nameof(strataGranularity)}' is not supported.");
            case CompressGranularity.Minute:
                return TimeSpan.FromMinutes(1);
            case CompressGranularity.Hour:
                return TimeSpan.FromHours(1);
            case CompressGranularity.Day:
                return TimeSpan.FromDays(1);
            case CompressGranularity.Week:
                return TimeSpan.FromDays(7);
            case CompressGranularity.Month:
                return TimeSpan.FromDays(30);
            case CompressGranularity.Quarter:
                return TimeSpan.FromDays(90);
            case CompressGranularity.Year:
                return TimeSpan.FromDays(365);
            default:
                throw new ArgumentOutOfRangeException(nameof(strataGranularity), strataGranularity, null);
        }
    }
}