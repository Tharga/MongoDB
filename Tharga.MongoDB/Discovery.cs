using System;

namespace Tharga.MongoDB;

[Flags]
public enum Discovery
{
    Database = 1,
    Registration = 2,
    Monitor = 4,
    Remote = 8
}