using System;

namespace Tharga.MongoDB.Lockable;

[Flags]
public enum LockMode
{
    Locked = 1,
    Exception = 2
}