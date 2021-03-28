using Newtonsoft.Json.Serialization;
using System;

namespace CWS.Domain.Shared.Json
{
   
    /// <summary>
    /// Encontra tipos sem olhar para a assembly
    /// </summary>
    public class JsonBinderWithoutAssembly : ISerializationBinder
    {
        public void BindToName(Type serializedType, out string assemblyName, out string typeName)
        {
            typeName = serializedType.FullName;
            assemblyName = null;
        }

        public Type BindToType(string assemblyName, string typeName)
        {
            return Type.GetType(typeName);
        }
        
    }
}
