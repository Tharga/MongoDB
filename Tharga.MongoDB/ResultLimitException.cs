using System;

namespace Tharga.MongoDB;

public class ResultLimitException : Exception
{
    public ResultLimitException(int resultLimit)
        : base($"The result limit of '{resultLimit}' was reached. To access more items use the method 'GetPageAsync' instead.")
    {
        ResultLimit = resultLimit;
    }

    public int ResultLimit { get; }
}