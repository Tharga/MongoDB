using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Tharga.MongoDB;

public class ActionEventArgs : EventArgs
{
    internal ActionEventArgs(ActionData action, ContextData context)
    {
        Context = context;
        Action = action with
        {
            Level = action.Level ?? (action.Exception != null ? LogLevel.Error : LogLevel.Information),
            Message = action.Message ?? action.Exception?.Message ?? $"Executed {action.Operation ?? "function"}.",
            Data = action.Data ?? new Dictionary<string, object>()
        };

        if (Action.Exception != null)
        {
            foreach (DictionaryEntry item in Action.Exception.Data)
            {
                Action.Data.TryAdd(item.Key.ToString(), item.Value);
            }
        }
    }

    public ActionData Action { get; }
    public ContextData Context { get; }

    public record ActionData
    {
        internal ActionData()
        {
        }

        public string Operation { get; init; }
        public TimeSpan Elapsed { get; init; }
        public int? ItemCount { get; init; }
        public LogLevel? Level { get; init; }
        public string Message { get; init; }
        public Exception Exception { get; init; }
        public Dictionary<string, object> Data { get; init; }
    }

    public record ContextData
    {
        internal ContextData()
        {
        }

        public string ServerName { get; init; }
        public string DatabaseName { get; init; }
        public string CollectionName { get; init; }
        public string EntityType { get; init; }
        public string CollectionType { get; init; }
    }
}