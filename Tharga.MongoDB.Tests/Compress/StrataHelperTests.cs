using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Tharga.MongoDB.Compress;
using Xunit;

namespace Tharga.MongoDB.Tests.Compress;

public class StrataHelperTests
{
    [Theory]
    [InlineData("0:00:00", CompressGranularity.None)]
    [InlineData("0:01:00", CompressGranularity.Minute)]
    [InlineData("1:00:00", CompressGranularity.Hour)]
    [InlineData("1.0:00:00", CompressGranularity.Day)]
    [InlineData("31.0:00:00", CompressGranularity.Month)]
    [InlineData("365.0:00:00", CompressGranularity.Year)]
    public void GetAge(string timestampString, CompressGranularity expected)
    {
        //Arrange
        var timestamp = DateTime.UtcNow.Subtract(TimeSpan.Parse(timestampString));

        //Act
        var result = StrataHelper.GetAge(timestamp);

        //Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetEmptyStrata(bool n)
    {
        //Arrange

        //Act
        var strata = StrataHelper.GetStrata(n ? null : new Strata[] { }, DateTime.UtcNow);

        //Assert
        strata.Should().BeNull();
    }

    [Theory]
    [InlineData("0:00:00")]
    [InlineData("0:01:00")]
    [InlineData("1:00:00")]
    [InlineData("1.0:00:00")]
    [InlineData("31.0:00:00")]
    [InlineData("365.0:00:00")]
    public void AlwaysCompress(string timestampString)
    {
        //Arrange
        var timestamp = DateTime.UtcNow.Subtract(TimeSpan.Parse(timestampString));
        var stratas = new Strata[]
        {
            new Strata(CompressGranularity.Month)
        };

        //Act

        var strata = StrataHelper.GetStrata(stratas, timestamp);

        //Assert
        strata.Should().Be(stratas.Single());
    }

    [Theory]
    [InlineData("0:00:00", false)]
    [InlineData("0:01:00", false)]
    [InlineData("1:00:00", false)]
    [InlineData("1.0:00:00", false)]
    [InlineData("31.0:00:00", true)]
    [InlineData("365.0:00:00", true)]
    public void CompressAfterOneMonth(string timestampString, bool compress)
    {
        //Arrange
        var timestamp = DateTime.UtcNow.Subtract(TimeSpan.Parse(timestampString));
        var stratas = new Strata[]
        {
            new Strata(CompressGranularity.Month,CompressGranularity.Month)
        };

        //Act

        var strata = StrataHelper.GetStrata(stratas, timestamp);

        //Assert
        if (compress)
        {
            strata.Should().Be(stratas.Single());
        }
        else
        {
            strata.Should().BeNull();
        }
    }

    [Theory]
    [InlineData("0:00:00", 1)]
    [InlineData("0:01:00", 1)]
    [InlineData("1:00:00", 1)]
    [InlineData("1.0:00:00", 1)]
    [InlineData("31.0:00:00", 2)]
    [InlineData("365.0:00:00", 2)]
    public void CompressWithTwoLevels(string timestampString, int level)
    {
        //Arrange
        var timestamp = DateTime.UtcNow.Subtract(TimeSpan.Parse(timestampString));
        var stratas = new Strata[]
        {
            new Strata(CompressGranularity.Day),
            new Strata(CompressGranularity.Month, CompressGranularity.Month)
        };

        //Act

        var strata = StrataHelper.GetStrata(stratas, timestamp);

        //Assert
        if (level == 1)
        {
            strata.CompressPer.Should().Be(CompressGranularity.Day);
        }
        else
        {
            strata.CompressPer.Should().Be(CompressGranularity.Month);
        }
    }

    [Theory]
    [MemberData(nameof(GetGranularities))]
    public void GetTimeSpan(CompressGranularity granularity)
    {
        //Arrange

        //Act
        var timeSpan = StrataHelper.GetTimeSpan(granularity);
        var age = StrataHelper.GetAge(DateTime.UtcNow.AddSeconds(-1).Subtract(timeSpan));

        //Assert
        age.Should().Be(granularity);
    }

    public static IEnumerable<object[]> GetGranularities()
    {
        foreach (var suit in Enum.GetValues<CompressGranularity>())
        {
            yield return new object[] { suit };
        }
    }
}