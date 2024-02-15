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
        else if (age.TotalDays > 31)
            granularity = CompressGranularity.Month;
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
}