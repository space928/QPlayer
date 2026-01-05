using QPlayer.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;

namespace QPlayer.Models;

public class PolymorphicTypeResolver : DefaultJsonTypeInfoResolver
{
    public override JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        JsonTypeInfo jsonTypeInfo = base.GetTypeInfo(type, options);

        Type baseCueType = typeof(Cue);
        if (jsonTypeInfo.Type == baseCueType)
        {
            jsonTypeInfo.PolymorphismOptions = new JsonPolymorphismOptions()
            {
                TypeDiscriminatorPropertyName = "$type",
                IgnoreUnrecognizedTypeDiscriminators = true,
                UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization,
            };
            var registeredTypes = CueFactory.RegisteredCueTypes.Select(x => new JsonDerivedType(x.modelType, x.name));
            foreach (var typeInfo in registeredTypes)
                jsonTypeInfo.PolymorphismOptions.DerivedTypes.Add(typeInfo);
        }

        return jsonTypeInfo;
    }
}
