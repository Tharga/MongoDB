using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Tharga.MongoDB.Disk;

public static class ExplainExtensions
{
    public static ExplainProvider DetectProvider(this BsonDocument doc)
    {
        if (doc.Contains("stages"))
        {
            return ExplainProvider.CosmosMongoApi;
        }

        if (doc.Contains("queryPlanner"))
        {
            return ExplainProvider.MongoDb;
        }

        throw new NotSupportedException("Unknown explain format");
    }

    public static ExplainExecutionSummary ParseMongoExecution(this BsonDocument doc)
    {
        var stats = doc.GetValue("executionStats", null)?.AsBsonDocument;
        if (stats == null)
        {
            return null;
        }

        return new ExplainExecutionSummary
        {
            ExecutionTimeMs = stats.GetValue("executionTimeMillis", BsonNull.Value).ToNullableInt64(),
            DocsExamined = stats.GetValue("totalDocsExamined", BsonNull.Value).ToNullableInt64(),
            KeysExamined = stats.GetValue("totalKeysExamined", BsonNull.Value).ToNullableInt64(),
            Returned = stats.GetValue("nReturned", BsonNull.Value).ToNullableInt64()
        };
    }

    public static ExplainExecutionSummary ParseCosmosExecution(this BsonDocument doc)
    {
        var stage = doc["stages"][0]["details"].AsBsonDocument;
        var metrics = stage.GetValue("queryMetrics", null)?.AsBsonDocument;

        return new ExplainExecutionSummary
        {
            ExecutionTimeMs = metrics?.GetValue("totalQueryExecutionTimeMS", BsonNull.Value).ToNullableInt64(),
            DocsExamined = metrics?.GetValue("retrievedDocumentCount", BsonNull.Value).ToNullableInt64(),
            Returned = metrics?.GetValue("outputDocumentCount", BsonNull.Value).ToNullableInt64(),
            RequestCharge = doc.GetValue("totalRequestCharge", BsonNull.Value).ToNullableDouble()
        };
    }

    public static JsonElement ToJsonElement(this BsonDocument document)
    {
        var json = document.ToJson();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static long? ToNullableInt64(this BsonValue value)
    {
        if (value == null || value.IsBsonNull)
        {
            return null;
        }

        return value.ToInt64();
    }

    private static double? ToNullableDouble(this BsonValue value)
    {
        if (value == null || value.IsBsonNull)
        {
            return null;
        }

        return value.ToDouble();
    }
}

public sealed class ExplainQuerySummary
{
    public JsonElement? Filter { get; init; }
    public JsonElement? Sort { get; init; }
    public JsonElement? Projection { get; init; }
    public int? Skip { get; init; }
    public int? Limit { get; init; }
}

public sealed class ExplainResponse
{
    public ExplainProvider Provider { get; init; }
    public ExplainQuerySummary Query { get; init; }
    public ExplainExecutionSummary Execution { get; init; }
    public JsonElement Raw { get; init; }
}

//public sealed class ExplainResponse
//{
//    public ExplainProvider Provider { get; init; }

//    public ExplainQuerySummary Query { get; init; }

//    public ExplainIndexSummary Index { get; init; }

//    public ExplainExecutionSummary Execution { get; init; }

//    public BsonDocument Raw { get; init; }

//    public static JsonElement ToJsonElement(BsonDocument document)
//    {
//        var json = document.ToJson();
//        using var doc = JsonDocument.Parse(json);
//        return doc.RootElement.Clone();
//    }
//}

public enum ExplainProvider
{
    MongoDb,
    CosmosMongoApi
}

//public sealed class ExplainQuerySummary
//{
//    public int? Limit { get; init; }
//    public int? Skip { get; init; }
//    public BsonDocument Filter { get; init; }
//    public BsonDocument Sort { get; init; }
//    public BsonDocument Projection { get; init; }
//}

public sealed class ExplainIndexSummary
{
    public string WinningIndex { get; init; }
    public IReadOnlyList<string> IndexedPaths { get; init; }
}

public sealed class ExplainExecutionSummary
{
    public long? ExecutionTimeMs { get; init; }
    public long? DocsExamined { get; init; }
    public long? KeysExamined { get; init; }
    public long? Returned { get; init; }

    // Cosmos-only
    public double? RequestCharge { get; init; }
}

//public class Rootobject
//{
//    //public Queryplanner queryPlanner { get; set; }
//    //public Executionstats executionStats { get; set; }
//    //public Serverinfo serverInfo { get; set; }
//    //public Clustertime clusterTime { get; set; }
//    //public Operationtime operationTime { get; set; }
//    public decimal ok { get; set; }
//}

////public class Rootobject
////{
////    //public string command { get; set; }
////    //public Stage[] stages { get; set; }
////    //public float estimatedDelayFromRateLimitingInMilliseconds { get; set; }
////    //public bool retriedDueToRateLimiting { get; set; }
////    //public float totalRequestCharge { get; set; }
////    //public Continuation continuation { get; set; }
////    //public string ActivityId { get; set; }
////    public float ok { get; set; }
////}

//// ---> Revisit

////public class Queryplanner
////{
////    public int plannerVersion { get; set; }
////    public string _namespace { get; set; }
////    public bool indexFilterSet { get; set; }
////    public Parsedquery parsedQuery { get; set; }
////    public Winningplan winningPlan { get; set; }
////    public object[] rejectedPlans { get; set; }
////}

////public class Parsedquery
////{
////    public ApplicationinfoApplicationid ApplicationInfoApplicationId { get; set; }
////}

////public class ApplicationinfoApplicationid
////{
////    public In[] _in { get; set; }
////}

////public class In
////{
////    public Binary binary { get; set; }
////}

////public class Binary
////{
////    public string base64 { get; set; }
////    public string subType { get; set; }
////}

////public class Winningplan
////{
////    public string stage { get; set; }
////    public int limitAmount { get; set; }
////    public Inputstage inputStage { get; set; }
////}

////public class Inputstage
////{
////    public string stage { get; set; }
////    public Filter filter { get; set; }
////    public Inputstage1 inputStage { get; set; }
////}

////public class Filter
////{
////    public ApplicationinfoApplicationid1 ApplicationInfoApplicationId { get; set; }
////}

////public class ApplicationinfoApplicationid1
////{
////    public In1[] _in { get; set; }
////}

////public class In1
////{
////    public Binary1 binary { get; set; }
////}

////public class Binary1
////{
////    public string base64 { get; set; }
////    public string subType { get; set; }
////}

////public class Inputstage1
////{
////    public string stage { get; set; }
////    public Keypattern keyPattern { get; set; }
////    public string indexName { get; set; }
////    public bool isMultiKey { get; set; }
////    public Multikeypaths multiKeyPaths { get; set; }
////    public bool isUnique { get; set; }
////    public bool isSparse { get; set; }
////    public bool isPartial { get; set; }
////    public int indexVersion { get; set; }
////    public string direction { get; set; }
////    public Indexbounds indexBounds { get; set; }
////}

////public class Keypattern
////{
////    public int _id { get; set; }
////}

////public class Multikeypaths
////{
////    public object[] _id { get; set; }
////}

////public class Indexbounds
////{
////    public string[] _id { get; set; }
////}

////public class Executionstats
////{
////    public bool executionSuccess { get; set; }
////    public int nReturned { get; set; }
////    public int executionTimeMillis { get; set; }
////    public int totalKeysExamined { get; set; }
////    public int totalDocsExamined { get; set; }
////    public Executionstages executionStages { get; set; }
////}

////public class Executionstages
////{
////    public string stage { get; set; }
////    public int nReturned { get; set; }
////    public int executionTimeMillisEstimate { get; set; }
////    public int works { get; set; }
////    public int advanced { get; set; }
////    public int needTime { get; set; }
////    public int needYield { get; set; }
////    public int saveState { get; set; }
////    public int restoreState { get; set; }
////    public int isEOF { get; set; }
////    public int limitAmount { get; set; }
////    public Inputstage2 inputStage { get; set; }
////}

////public class Inputstage2
////{
////    public string stage { get; set; }
////    public Filter1 filter { get; set; }
////    public int nReturned { get; set; }
////    public int executionTimeMillisEstimate { get; set; }
////    public int works { get; set; }
////    public int advanced { get; set; }
////    public int needTime { get; set; }
////    public int needYield { get; set; }
////    public int saveState { get; set; }
////    public int restoreState { get; set; }
////    public int isEOF { get; set; }
////    public int docsExamined { get; set; }
////    public int alreadyHasObj { get; set; }
////    public Inputstage3 inputStage { get; set; }
////}

////public class Filter1
////{
////    public ApplicationinfoApplicationid2 ApplicationInfoApplicationId { get; set; }
////}

////public class ApplicationinfoApplicationid2
////{
////    public In2[] _in { get; set; }
////}

////public class In2
////{
////    public Binary2 binary { get; set; }
////}

////public class Binary2
////{
////    public string base64 { get; set; }
////    public string subType { get; set; }
////}

////public class Inputstage3
////{
////    public string stage { get; set; }
////    public int nReturned { get; set; }
////    public int executionTimeMillisEstimate { get; set; }
////    public int works { get; set; }
////    public int advanced { get; set; }
////    public int needTime { get; set; }
////    public int needYield { get; set; }
////    public int saveState { get; set; }
////    public int restoreState { get; set; }
////    public int isEOF { get; set; }
////    public Keypattern1 keyPattern { get; set; }
////    public string indexName { get; set; }
////    public bool isMultiKey { get; set; }
////    public Multikeypaths1 multiKeyPaths { get; set; }
////    public bool isUnique { get; set; }
////    public bool isSparse { get; set; }
////    public bool isPartial { get; set; }
////    public int indexVersion { get; set; }
////    public string direction { get; set; }
////    public Indexbounds1 indexBounds { get; set; }
////    public int keysExamined { get; set; }
////    public int seeks { get; set; }
////    public int dupsTested { get; set; }
////    public int dupsDropped { get; set; }
////}

////public class Keypattern1
////{
////    public int _id { get; set; }
////}

////public class Multikeypaths1
////{
////    public object[] _id { get; set; }
////}

////public class Indexbounds1
////{
////    public string[] _id { get; set; }
////}

////public class Serverinfo
////{
////    public string host { get; set; }
////    public int port { get; set; }
////    public string version { get; set; }
////    public string gitVersion { get; set; }
////}

////public class Clustertime
////{
////    public Clustertime clusterTime { get; set; }
////    public Signature signature { get; set; }
////}

////public class Clustertime
////{
////    public Timestamp timestamp { get; set; }
////}

////public class Timestamp
////{
////    public int t { get; set; }
////    public int i { get; set; }
////}

////public class Signature
////{
////    public Hash hash { get; set; }
////    public int keyId { get; set; }
////}

////public class Hash
////{
////    public Binary3 binary { get; set; }
////}

////public class Binary3
////{
////    public string base64 { get; set; }
////    public string subType { get; set; }
////}

////public class Operationtime
////{
////    public Timestamp1 timestamp { get; set; }
////}

////public class Timestamp1
////{
////    public int t { get; set; }
////    public int i { get; set; }
////}

//////--->

////public class Continuation
////{
////    public bool hasMore { get; set; }
////}

////public class Stage
////{
////    public string stage { get; set; }
////    public float timeInclusiveMS { get; set; }
////    public float timeExclusiveMS { get; set; }
////    public int _in { get; set; }
////    public int _out { get; set; }
////    public Dependency dependency { get; set; }
////    public Details details { get; set; }
////}

////public class Dependency
////{
////    public int getNextPageCount { get; set; }
////    public int count { get; set; }
////    public float time { get; set; }
////    public int bytes { get; set; }
////}

////public class Details
////{
////    public string database { get; set; }
////    public string collection { get; set; }
////    public Query query { get; set; }
////    public Indexusage indexUsage { get; set; }
////    public int skip { get; set; }
////    public int limit { get; set; }
////    public Sort sort { get; set; }
////    public Shardinformation[] shardInformation { get; set; }
////    public Querymetrics queryMetrics { get; set; }
////}

////public class Query
////{
////    public ApplicationinfoApplicationid ApplicationInfoApplicationId { get; set; }
////}

////public class ApplicationinfoApplicationid
////{
////    public In[] _in { get; set; }
////}

////public class In
////{
////    public Binary binary { get; set; }
////}

////public class Binary
////{
////    public string base64 { get; set; }
////    public string subType { get; set; }
////}

////public class Indexusage
////{
////    public Pathsindexed pathsIndexed { get; set; }
////    public Pathsnotindexed pathsNotIndexed { get; set; }
////}

////public class Pathsindexed
////{
////    public string[] individualIndexes { get; set; }
////    public object[] compoundIndexes { get; set; }
////}

////public class Pathsnotindexed
////{
////    public object[] individualIndexes { get; set; }
////    public object[] compoundIndexes { get; set; }
////}

////public class Sort
////{
////    public int _id { get; set; }
////}

////public class Querymetrics
////{
////    public int retrievedDocumentCount { get; set; }
////    public int retrievedDocumentSizeBytes { get; set; }
////    public int outputDocumentCount { get; set; }
////    public int outputDocumentSizeBytes { get; set; }
////    public float indexHitRatio { get; set; }
////    public float totalQueryExecutionTimeMS { get; set; }
////    public Querypreparationtimes queryPreparationTimes { get; set; }
////    public float indexLookupTimeMS { get; set; }
////    public float documentLoadTimeMS { get; set; }
////    public float vmExecutionTimeMS { get; set; }
////    public Runtimeexecutiontimes runtimeExecutionTimes { get; set; }
////    public float documentWriteTimeMS { get; set; }
////}

////public class Querypreparationtimes
////{
////    public float queryCompilationTimeMS { get; set; }
////    public float logicalPlanBuildTimeMS { get; set; }
////    public float physicalPlanBuildTimeMS { get; set; }
////    public float queryOptimizationTimeMS { get; set; }
////}

////public class Runtimeexecutiontimes
////{
////    public float queryEngineExecutionTimeMS { get; set; }
////    public float systemFunctionExecutionTimeMS { get; set; }
////    public int userDefinedFunctionExecutionTimeMS { get; set; }
////}

////public class Shardinformation
////{
////    public string activityId { get; set; }
////    public string shardKeyRangeId { get; set; }
////    public float durationMS { get; set; }
////    public int preemptions { get; set; }
////    public int outputDocumentCount { get; set; }
////    public int retrievedDocumentCount { get; set; }
////}
