# Tharga.MongoDb
This package was broken out of a product that needs the possibility of dynamic naming of databases and collections.
I also helps with structuring what functions are to be accessed for different types.

It also have some aditional features for Atlas MongoDB like limiting the size of the responses and opening the firewall.

## Get started
Install the nuget package `Tharga.MongoDb`.

### Register to use
Register this package at startup by calling `AddMongoDb` as an extension to `IServiceCollection`.

```
public void ConfigureServices(IServiceCollection services)
{
    services.AddMongoDb();
}
```

By default the configuration setting `ConnectionStrings:Default` is used to get the connection string.
Customize by providing `DatabaseOptions` to `AddMongoDb`.

### Create entities, repositories and collections.

The simplest way is to have the repository implement the collection directly.
```
public class MySimpleRepo : DiskRepositoryCollectionBase<MyEntity, ObjectId>
{
    public MySimpleRepo(IMongoDbServiceFactory mongoDbServiceFactory)
        : base(mongoDbServiceFactory)
    {
    }
}

public record MyEntity : EntityBase<ObjectId>
{
}
```

The more complex way that gives more control is to implement one class for the repo and another for the collection.
This way you can control what methods repo methods are exposed to consumers.
Here implemented with interfaces and the collection made internal.
```
public interface IMySimpleRepo : IRepository
{
    public Task<MyEntity> GetFirstOrDefaultAsync();
}

public class MySimpleRepo : IMySimpleRepo
{
    private readonly IMySimpleCollection _mySimpleCollection;

    public MySimpleRepo(IMySimpleCollection mySimpleCollection)
    {
        _mySimpleCollection = mySimpleCollection;
    }

    public Task<MyEntity> GetFirstOrDefaultAsync()
    {
        return _mySimpleCollection.GetOneAsync(x => true);
    }
}

public interface IMySimpleCollection : IRepositoryCollection<MyEntity, ObjectId>
{
}

internal class MySimpleCollection : DiskRepositoryCollectionBase<MyEntity, ObjectId>, IMySimpleCollection
{
    public MySimpleCollection(IMongoDbServiceFactory mongoDbServiceFactory)
        : base(mongoDbServiceFactory)
    {
    }

    public Task<MyEntity> GetFirstOrDefaultAsync()
    {
        throw new NotImplementedException();
    }
}

public record MyEntity : EntityBase<ObjectId>
{
}
```

---

## Simple Console Sample
This is a simple demo for a console application written in .NET 7.
The following nuget packages are used.
- Tharga.MongoDb
- Microsoft.Extensions.Hosting

```
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using Tharga.MongoDb;
using Tharga.MongoDb.Disk;

var services = new ServiceCollection();
services.AddMongoDb(o => o.ConnectionStringLoader = _ => "mongodb://localhost:27017/SimpleConsoleSample");

var serviceProvider = services.BuildServiceProvider();

var simpleRepo = serviceProvider.GetService<MySimpleRepo>();
await simpleRepo!.AddAsync(new MyEntity());
var oneItem = await simpleRepo.GetOneAsync(x => true);

Console.WriteLine($"Got item with id '{oneItem.Id}' from the database.");

public class MySimpleRepo : DiskRepositoryCollectionBase<MyEntity, ObjectId>
{
    public MySimpleRepo(IMongoDbServiceFactory mongoDbServiceFactory)
        : base(mongoDbServiceFactory)
    {
    }
}

public record MyEntity : EntityBase<ObjectId>
{
}
```

---

## More details

### Configuration
Configuring can be done in `appsettings.json` or by code. Code is always used first value by value.
If using multiple (named) databases, configuration will always use the named version first if there is one and then use the general fallback value.
This is the order used, value by value.
1. Named configuration from code
1. General configuration from code
1. Named configuration from IConfiguration
1. General configuration from IConfiguration
1. Default values

#### Example of configuration by `appsettings.json`.
When the 'Default' database is used, the result limit will be 100, for the 'Other' database the result limit will be 200.
If another database is implemented, the fallback of 1000 will be used as result limit.

The 'Default' database will have the firewall opened, if hosted in Atlas MongoDB.

```
  "ConnectionStrings": {
    "Default": "mongodb://localhost:27017/Tharga{environment}_Sample{part}",
    "Other": "mongodb://localhost:27017/Tharga{environment}_Sample_Other{part}"
  },
  "MongoDb": {
    "Default": {
      "AccessInfo": {
        "PublicKey": "[PublicKey]",
        "PrivateKey": "[PrivateKey]",
        "ClusterId": "[ClusterId]"
      },
      "ResultLimit": 100,
      "AutoClean": true,
      "CleanOnStartup": true,
      "DropEmptyCollections": true
    },
    "Other": {
      "ResultLimit": 200
    },
    "ResultLimit": 1000
    "AutoClean": false,
    "CleanOnStartup": false,
    "DropEmptyCollections": false
  }
```

#### Example of configuration by code.
This would be the same configuration as from the example above.
```
        services.AddMongoDb(o =>
        {
            o.ConnectionStringLoader = name =>
            {
                return (string)name switch
                {
                    "Default" => "mongodb://localhost:27017/Tharga{environment}_Sample{part}",
                    "Other" => "mongodb://localhost:27017/Tharga{environment}_Sample_Other{part}",
                    _ => throw new ArgumentException($"Unknown configuration name '{name}'.")
                };
            };
            o.Configuration = new MongoDbConfigurationTree
            {
                Configurations = new Dictionary<ConfigurationName, MongoDbConfiguration>
                {
                    {
                        "Default", new MongoDbConfiguration
                        {
                            AccessInfo = new MongoDbApiAccess
                            {
                                PublicKey = "[PublicKey]",
                                PrivateKey = "[PrivateKey]",
                                ClusterId = "[ClusterId]"
                            },
                            ResultLimit = 100,
                            AutoClean = true,
                            CleanOnStartup = true,
                            DropEmptyCollections = true
                        }
                    },
                    {
                        "Other", new MongoDbConfiguration
                        {
                            ResultLimit = 200
                        }
                    }
                },
                ResultLimit = 1000,
                AutoClean = false,
                CleanOnStartup = false,
                DropEmptyCollections = false
            };
        });
```

### Customize collections
Properties for classes deriving from `RepositoryCollectionBase<,>` can be customised directly by overriding the default behaviour of the code or configuration.

By default the name of the collection is the same as the type name of the entity.
To have a different name the property `CollectionName` can be overridden.

The name of the database can be built up dynamically, use `DatabasePart` to do so.
Read more about this in the section [MongoUrl Builder](#mongourlbuilder).

Override property `ConfigurationName` to use different database than default (or set as default in `DatabaseOptions`).
This makes it possible to use multiple databases from the same application.

The properties `AutoClean`, `CleanOnStartup`, `DropEmptyCollections` and `ResultLimit` can be overridden by collection to be different from the configuration.

To automatically register known types when using multiple types in the same collection, provide a value for `Types`.

Create `Indicies` by overriding the property in your collection class.
The list of `Indicies` is applied befor the first record is added to the collection.
It is also reviewed once every time the application starts, removing `Indicies` that no longer exists and creates new ones if the code have changed.

### MongoUrl Builder
The `MongoUrl` is created by a built in implementation of `IMongoUrlBuilder`. It takes the raw version and parses variables to build `MongoUrl`.

Two variables are supported `{environment}` and `{part}`.

To dynamicaly change the name of the database `{part}` can be used. It can be used as an override to a collection or provided as a variable in `DatabaseContext` together with [CollectionProvider](#collectionprovider).

For `{environment}` the value will be ommitted when it is set to 'Production'.

Both variables will get a leading character of '_'.

Example for Development with the databasePart = MyPart.
`mongodb://localhost:27017/Tharga{environment}_Sample{part}` --> `mongodb://localhost:27017/Tharga_Development_Sample_MyPart`

#### Custom MongoUrl Builder
If there is a need for a custom string builder, implement the interface `IMongoUrlBuilder` and register with the IOC and that will be used instead of the built in version.
Register your own version of IMongoUrlBuilder in IOC.
```
services.AddTransient<IMongoUrlBuilder, MyMongoUrlBuilder>();
```


<!--### CollectionProvider
### Multiple databases
### Buffer vs Disk
### Cleaning of entities-->

---

## MongoDB Result Limit
It is possible to se t a hard limit for the number of documents returned. If the limit is reached `ResultLimitException` is thrown.
For large result-sets, use the method `GetPageAsync` to get the `ResultLimit` on each page of the result.

```
{
  "MongoDb": {
    "ResultLimit": 500
  }
}
```