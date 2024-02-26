﻿namespace Tharga.MongoDB.Compress;

public enum CompressGranularity
{
    None = 0,
    Minute = 1,
    Hour = 2,
    Day = 3,
    Week = 4,
    Month = 5,
    Quarter = 6,
    Year = 7,
    Drop = 100
}