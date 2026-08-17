using System.Text.Json.Serialization;

namespace WinFIMLog.IO.Security
{
    [JsonSerializable(typeof(AccessControlList))]
    internal partial class AclJsonSerializerContext : JsonSerializerContext;
}
